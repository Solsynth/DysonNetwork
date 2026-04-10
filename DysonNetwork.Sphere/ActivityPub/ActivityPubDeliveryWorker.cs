using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using NATS.Client.Core;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubDeliveryWorker(
    INatsConnection nats,
    IServiceProvider serviceProvider,
    IHttpClientFactory httpClientFactory,
    IOptions<ActivityPubDeliveryOptions> options,
    ILogger<ActivityPubDeliveryWorker> logger,
    IClock clock
) : BackgroundService
{
    public const string QueueName = "activitypub_delivery_queue";
    private const string QueueGroup = "activitypub_delivery_workers";
    private readonly List<Task> _consumerTasks = [];
    private readonly ConcurrentDictionary<string, HttpClient> _httpClients = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting {ConsumerCount} ActivityPub delivery consumers", options.Value.ConsumerCount);

        for (var i = 0; i < options.Value.ConsumerCount; i++)
            _consumerTasks.Add(Task.Run(() => RunConsumerAsync(stoppingToken), stoppingToken));

        await Task.WhenAll(_consumerTasks);
    }

    private async Task RunConsumerAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ActivityPub delivery consumer started");

        await foreach (var msg in nats.SubscribeAsync<byte[]>(QueueName, queueGroup: QueueGroup, cancellationToken: stoppingToken))
        {
            try
            {
                // Try to parse as batch first, then as single message
                var batchMessage = InfraObjectCoder.ConvertByteStringToObject<ActivityPubDeliveryBatchMessage>(ByteString.CopyFrom(msg.Data));
                if (batchMessage is not null && batchMessage.Deliveries.Count > 0)
                {
                    await ProcessBatchAsync(batchMessage, stoppingToken);
                }
                else
                {
                    var message = InfraObjectCoder.ConvertByteStringToObject<ActivityPubDeliveryMessage>(ByteString.CopyFrom(msg.Data));
                    if (message is not null)
                    {
                        await ProcessDeliveryAsync(message, stoppingToken);
                    }
                    else
                    {
                        logger.LogWarning("Invalid message format for ActivityPub delivery");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ActivityPub delivery consumer");
            }
        }
    }

    private async Task ProcessBatchAsync(ActivityPubDeliveryBatchMessage batch, CancellationToken cancellationToken)
    {
        logger.LogDebug("Processing batch of {Count} deliveries to {Inbox}", batch.Deliveries.Count, batch.InboxUri);

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var signatureService = scope.ServiceProvider.GetRequiredService<ActivityPubSignatureService>();

        var deliveryIds = batch.Deliveries.Select(d => d.DeliveryId).ToList();
        var deliveries = await db.ActivityPubDeliveries
            .Where(d => deliveryIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, cancellationToken);

        foreach (var message in batch.Deliveries)
        {
            if (!deliveries.TryGetValue(message.DeliveryId, out var delivery))
            {
                logger.LogWarning("Delivery record not found: {DeliveryId}", message.DeliveryId);
                continue;
            }

            delivery.Status = DeliveryStatus.Processing;
            delivery.LastAttemptAt = clock.GetCurrentInstant();
        }
        await db.SaveChangesAsync(cancellationToken);

        var tasks = batch.Deliveries.Select(async message =>
        {
            if (!deliveries.TryGetValue(message.DeliveryId, out var delivery))
                return;

            try
            {
                var success = await SendActivityToInboxAsync(
                    message.Activity,
                    message.InboxUri,
                    message.ActorUri,
                    signatureService,
                    httpClientFactory,
                    logger,
                    cancellationToken
                );

                if (success.IsSuccessStatusCode)
                {
                    delivery.Status = DeliveryStatus.Sent;
                    delivery.SentAt = clock.GetCurrentInstant();
                    delivery.ResponseStatusCode = success.StatusCode.ToString();
                }
                else
                {
                    var retryAfter = success.Headers.TryGetValues("Retry-After", out var values) && 
                                     values.FirstOrDefault() is string ra && 
                                     double.TryParse(ra, out var seconds) 
                        ? TimeSpan.FromSeconds(seconds) 
                        : (TimeSpan?)null;
                    var shouldRetry = ShouldRetry(success.StatusCode, retryAfter);
                    delivery.ResponseStatusCode = success.StatusCode.ToString();
                    delivery.ErrorMessage = await success.Content.ReadAsStringAsync(cancellationToken);

                    if (shouldRetry && delivery.RetryCount < options.Value.MaxRetries)
                    {
                        delivery.Status = DeliveryStatus.Failed;
                        delivery.RetryCount++;
                        delivery.NextRetryAt = CalculateNextRetryAt(delivery.RetryCount, clock, retryAfter);
                    }
                    else
                    {
                        delivery.Status = DeliveryStatus.ExhaustedRetries;
                    }
                }
            }
            catch (Exception ex)
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.ErrorMessage = ex.Message;

                if (delivery.RetryCount < options.Value.MaxRetries)
                {
                    delivery.RetryCount++;
                    delivery.NextRetryAt = CalculateNextRetryAt(delivery.RetryCount, clock);
                }
                else
                {
                    delivery.Status = DeliveryStatus.ExhaustedRetries;
                }
            }
        });

        await Task.WhenAll(tasks);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Processed batch of {Count} deliveries to {Inbox}", batch.Deliveries.Count, batch.InboxUri);
    }

    private async Task ProcessDeliveryAsync(ActivityPubDeliveryMessage message, CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var signatureService = scope.ServiceProvider.GetRequiredService<ActivityPubSignatureService>();

        logger.LogDebug("Processing ActivityPub delivery {DeliveryId} to {Inbox}", message.DeliveryId, message.InboxUri);

        var delivery = await db.ActivityPubDeliveries.FindAsync([message.DeliveryId], cancellationToken);
        if (delivery == null)
        {
            logger.LogWarning("Delivery record not found: {DeliveryId}", message.DeliveryId);
            return;
        }

        delivery.Status = DeliveryStatus.Processing;
        delivery.LastAttemptAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var success = await SendActivityToInboxAsync(
                message.Activity,
                message.InboxUri,
                message.ActorUri,
                signatureService,
                httpClientFactory,
                logger,
                cancellationToken
            );

            if (success.IsSuccessStatusCode)
            {
                delivery.Status = DeliveryStatus.Sent;
                delivery.SentAt = clock.GetCurrentInstant();
                delivery.ResponseStatusCode = success.StatusCode.ToString();
                logger.LogInformation("Successfully delivered activity {ActivityId} to {Inbox}",
                    message.ActivityId, message.InboxUri);
            }
            else
            {
                var retryAfter = success.Headers.TryGetValues("Retry-After", out var values) && 
                                 values.FirstOrDefault() is string ra && 
                                 double.TryParse(ra, out var seconds) 
                    ? TimeSpan.FromSeconds(seconds) 
                    : (TimeSpan?)null;
                var shouldRetry = ShouldRetry(success.StatusCode, retryAfter);
                delivery.ResponseStatusCode = success.StatusCode.ToString();
                delivery.ErrorMessage = await success.Content.ReadAsStringAsync(cancellationToken);

                if (shouldRetry && delivery.RetryCount < options.Value.MaxRetries)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.RetryCount++;
                    delivery.NextRetryAt = CalculateNextRetryAt(delivery.RetryCount, clock, retryAfter);
                    logger.LogWarning("Failed to deliver activity {ActivityId} to {Inbox}. Status: {Status}. Retry {RetryCount}/{MaxRetries} at {NextRetry}",
                        message.ActivityId, message.InboxUri, success.StatusCode, delivery.RetryCount, options.Value.MaxRetries, delivery.NextRetryAt);
                }
                else
                {
                    delivery.Status = DeliveryStatus.ExhaustedRetries;
                    logger.LogError("Exhausted retries for activity {ActivityId} to {Inbox}. Status: {Status}",
                        message.ActivityId, message.InboxUri, success.StatusCode);
                }
            }
        }
        catch (Exception ex)
        {
            delivery.Status = DeliveryStatus.Failed;
            delivery.ErrorMessage = ex.Message;

            if (delivery.RetryCount < options.Value.MaxRetries)
            {
                delivery.RetryCount++;
                delivery.NextRetryAt = CalculateNextRetryAt(delivery.RetryCount, clock);
                logger.LogError(ex, "Error delivering activity {ActivityId} to {Inbox}. Retry {RetryCount}/{MaxRetries} at {NextRetry}",
                    message.ActivityId, message.InboxUri, delivery.RetryCount, options.Value.MaxRetries, delivery.NextRetryAt);
            }
            else
            {
                delivery.Status = DeliveryStatus.ExhaustedRetries;
                logger.LogError(ex, "Exhausted retries for activity {ActivityId} to {Inbox}",
                    message.ActivityId, message.InboxUri);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendActivityToInboxAsync(
        Dictionary<string, object> activity,
        string inboxUrl,
        string actorUri,
        ActivityPubSignatureService signatureService,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var inboxUri = new Uri(inboxUrl);
        var hasQueryParams = inboxUri.Query.Length > 0;

        var response = await SendRequestAsync(inboxUrl, activity, actorUri, signatureService, httpClientFactory, logger, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && hasQueryParams)
        {
            logger.LogWarning("Got 401 from {Inbox} with query params, retrying without query params", inboxUrl);

            var baseUrl = $"{inboxUri.Scheme}://{inboxUri.Host}{inboxUri.AbsolutePath}";
            response = await SendRequestAsync(baseUrl, activity, actorUri, signatureService, httpClientFactory, logger, cancellationToken);
        }

        return response;
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(
        string inboxUrl,
        Dictionary<string, object> activity,
        string actorUri,
        ActivityPubSignatureService signatureService,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var json = JsonSerializer.Serialize(activity);
        var request = new HttpRequestMessage(HttpMethod.Post, inboxUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/activity+json")
        };

        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bodyBytes);
        var digest = $"SHA-256={Convert.ToBase64String(hash)}";
        request.Headers.Add("Digest", digest);
        request.Headers.Host = new Uri(inboxUrl).Host;

        logger.LogDebug("Sending request to {Inbox}", inboxUrl);

        if (!string.IsNullOrEmpty(actorUri))
        {
            await signatureService.SignOutgoingRequestAsync(request, actorUri);
        }

        var response = await client.SendAsync(request, cancellationToken);
        logger.LogDebug("Response from {Inbox}. Status: {Status}", inboxUrl, response.StatusCode);

        return response;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode, TimeSpan? retryAfter = null)
    {
        if (statusCode == (HttpStatusCode)429 && retryAfter.HasValue)
            return true;

        return statusCode == HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == (HttpStatusCode)429;
    }

    private static Instant CalculateNextRetryAt(int retryCount, IClock clock, TimeSpan? retryAfter = null)
    {
        const int maxDelaySeconds = 900;
        double delaySec;
        
        if (retryAfter.HasValue)
        {
            var retryDelay = retryAfter.Value.TotalSeconds;
            delaySec = Math.Min(maxDelaySeconds, Math.Max(1, retryDelay));
        }
        else
        {
            var baseDelaySeconds = 1;
            delaySec = Math.Min(maxDelaySeconds, baseDelaySeconds * (int)Math.Pow(2, retryCount - 1));
        }
        
        var jitter = Random.Shared.NextDouble() * 0.3 * delaySec;
        return clock.GetCurrentInstant() + Duration.FromSeconds(delaySec + jitter);
    }
}

public class ActivityPubDeliveryOptions
{
    public const string SectionName = "ActivityPubDelivery";
    public int MaxRetries { get; set; } = 5;
    public int ConsumerCount { get; set; } = 4;
}
