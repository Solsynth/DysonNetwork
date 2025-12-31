using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
                var message = GrpcTypeHelper.ConvertByteStringToObject<ActivityPubDeliveryMessage>(ByteString.CopyFrom(msg.Data));
                if (message is not null)
                {
                    await ProcessDeliveryAsync(message, stoppingToken);
                }
                else
                {
                    logger.LogWarning("Invalid message format for ActivityPub delivery");
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
                var shouldRetry = ShouldRetry(success.StatusCode);
                delivery.ResponseStatusCode = success.StatusCode.ToString();
                delivery.ErrorMessage = await success.Content.ReadAsStringAsync(cancellationToken);

                if (shouldRetry && delivery.RetryCount < options.Value.MaxRetries)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.RetryCount++;
                    delivery.NextRetryAt = CalculateNextRetryAt(delivery.RetryCount, clock);
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
        var client = httpClientFactory.CreateClient();
        var json = JsonSerializer.Serialize(activity);
        var request = new HttpRequestMessage(HttpMethod.Post, inboxUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/activity+json")
        };

        request.Headers.Date = DateTimeOffset.UtcNow;

        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bodyBytes);
        var digest = $"SHA-256={Convert.ToBase64String(hash)}";
        request.Headers.Add("Digest", digest);
        request.Headers.Host = new Uri(inboxUrl).Host;

        logger.LogDebug("Sending request to {Inbox}", inboxUrl);

        var signatureHeaders = await signatureService.SignOutgoingRequest(request, actorUri);

        var signatureString = $"keyId=\"{signatureHeaders["keyId"]}\"," +
                              $"algorithm=\"{signatureHeaders["algorithm"]}\"," +
                              $"headers=\"{signatureHeaders["headers"]}\"," +
                              $"signature=\"{signatureHeaders["signature"]}\"";

        request.Headers.Add("Signature", signatureString);

        var response = await client.SendAsync(request, cancellationToken);
        logger.LogDebug("Response from {Inbox}. Status: {Status}", inboxUrl, response.StatusCode);

        return response;
    }

    private static bool ShouldRetry(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.InternalServerError ||
               statusCode == HttpStatusCode.BadGateway ||
               statusCode == HttpStatusCode.ServiceUnavailable ||
               statusCode == HttpStatusCode.GatewayTimeout ||
               statusCode == HttpStatusCode.RequestTimeout ||
               statusCode == (HttpStatusCode)429; // Too Many Requests
    }

    private static Instant CalculateNextRetryAt(int retryCount, IClock clock)
    {
        var baseDelaySeconds = 1;
        var maxDelaySeconds = 300;
        var delaySeconds = Math.Min(maxDelaySeconds, baseDelaySeconds * (int)Math.Pow(2, retryCount - 1));
        return clock.GetCurrentInstant() + Duration.FromSeconds(delaySeconds);
    }
}

public class ActivityPubDeliveryOptions
{
    public const string SectionName = "ActivityPubDelivery";
    public int MaxRetries { get; set; } = 5;
    public int ConsumerCount { get; set; } = 4;
}
