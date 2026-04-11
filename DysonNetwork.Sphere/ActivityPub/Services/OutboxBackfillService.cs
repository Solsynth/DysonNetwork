using System.Collections.Concurrent;
using System.Text.Json;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class OutboxBackfillService(
    AppDatabase db,
    IActorDiscoveryService discoveryService,
    ActivityPubQueueService queueService,
    ILogger<OutboxBackfillService> logger,
    IClock clock,
    IFederationMetricsService metricsService
)
{
    private readonly ConcurrentDictionary<string, DateTime> _actorBackfillLocks = new();
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMinutes(5);
    private static readonly Duration BackfillInterval = Duration.FromHours(24);

    public const string QueueName = "outbox_backfill_queue";

    public async Task EnqueueBackfillAsync(Guid actorId, string actorUri, string outboxUri, int maxItems = 100)
    {
        if (string.IsNullOrEmpty(outboxUri))
        {
            logger.LogDebug("Actor {ActorUri} has no outbox, skipping backfill", actorUri);
            return;
        }

        if (IsLocked(actorUri))
        {
            logger.LogDebug("Actor {ActorUri} is already being backfilled", actorUri);
            return;
        }

        var actor = await db.FediverseActors.FindAsync(actorId);
        if (actor == null)
        {
            logger.LogWarning("Actor not found for backfill: {ActorId}", actorId);
            return;
        }

        if (actor.OutboxFetchedAt.HasValue)
        {
            var timeSinceLastBackfill = clock.GetCurrentInstant() - actor.OutboxFetchedAt.Value;
            if (timeSinceLastBackfill < BackfillInterval)
            {
                logger.LogDebug("Actor {ActorUri} was recently backfilled, skipping", actorUri);
                return;
            }
        }

        var message = new ActivityPubOutboxBackfillMessage
        {
            ActorId = actorId,
            ActorUri = actorUri,
            OutboxUri = outboxUri,
            MaxItems = maxItems
        };

        logger.LogInformation("Enqueuing outbox backfill for {ActorUri}", actorUri);
        await queueService.EnqueueOutboxBackfillAsync(message);
    }

    public async Task ProcessBackfillAsync(ActivityPubOutboxBackfillMessage message, CancellationToken cancellationToken = default)
    {
        if (IsLocked(message.ActorUri))
        {
            logger.LogDebug("Actor {ActorUri} is already being processed", message.ActorUri);
            return;
        }

        LockActor(message.ActorUri);
        var postsFetched = 0;

        try
        {
            logger.LogInformation("Starting outbox backfill for {ActorUri}, max {MaxItems} items", 
                message.ActorUri, message.MaxItems);

            var collection = await FetchOutboxCollectionAsync(message.OutboxUri, cancellationToken);
            if (collection == null)
            {
                logger.LogWarning("Failed to fetch outbox collection from {OutboxUri}", message.OutboxUri);
                return;
            }

            var items = IterateCollection(collection, message.MaxItems).Take(message.MaxItems);
            
            await foreach (var activity in items.WithCancellation(cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var activityType = GetString(activity, "type");
                if (activityType == "Create")
                {
                    var objectData = GetObject(activity, "object");
                    var objectType = GetString(objectData, "type");
                    
                    if (objectType == "Note")
                    {
                        postsFetched++;
                        logger.LogDebug("Found post: {Id}", GetString(objectData, "id"));
                    }
                }
            }

            await UpdateActorBackfillTimeAsync(message.ActorId);
            metricsService.RecordOutboxBackfill(1);
            metricsService.RecordPostsFetched(postsFetched);

            logger.LogInformation("Completed outbox backfill for {ActorUri}, fetched {Count} posts", 
                message.ActorUri, postsFetched);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during outbox backfill for {ActorUri}", message.ActorUri);
        }
        finally
        {
            UnlockActor(message.ActorUri);
        }
    }

    private async Task<Dictionary<string, object>?> FetchOutboxCollectionAsync(string outboxUri, CancellationToken cancellationToken)
    {
        return await discoveryService.FetchActivityAsync(outboxUri, null);
    }

    public async IAsyncEnumerable<Dictionary<string, object>> IterateCollection(
        Dictionary<string, object> collection,
        int pageLimit = 10
    )
    {
        var visitedPages = new HashSet<string>();
        
        var firstItems = GetArrayItems(collection, "orderedItems");
        foreach (var item in firstItems)
        {
            yield return item;
        }

        var currentPage = GetNextPageUrl(collection);
        var pageCount = 0;

        while (currentPage != null && pageCount < pageLimit)
        {
            if (!visitedPages.Add(currentPage))
            {
                logger.LogWarning("Detected circular pagination in collection, stopping at {Url}", currentPage);
                break;
            }

            var page = await discoveryService.FetchActivityAsync(currentPage, null);
            if (page == null)
            {
                logger.LogWarning("Failed to fetch collection page: {Url}", currentPage);
                break;
            }

            var pageItems = GetArrayItems(page, "orderedItems");
            foreach (var item in pageItems)
            {
                yield return item;
            }

            currentPage = GetNextPageUrl(page);
            pageCount++;
        }
    }

    private static List<Dictionary<string, object>> GetArrayItems(Dictionary<string, object> dict, string key)
    {
        var result = new List<Dictionary<string, object>>();
        
        if (!dict.TryGetValue(key, out var value)) 
            return result;

        JsonElement? element = value switch
        {
            JsonElement e => e,
            Dictionary<string, object> obj when obj.TryGetValue(key, out var nested) && nested is JsonElement ne => ne,
            _ => null
        };

        if (element.HasValue && element.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.Value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var deserialized = JsonSerializer.Deserialize<Dictionary<string, object>>(item.GetRawText());
                    if (deserialized != null)
                    {
                        result.Add(deserialized);
                    }
                }
            }
        }

        return result;
    }

    private string? GetNextPageUrl(Dictionary<string, object> collection)
    {
        var next = GetString(collection, "next");
        var first = GetString(collection, "first");

        if (!string.IsNullOrEmpty(next))
            return next;
        
        if (!string.IsNullOrEmpty(first))
            return first;

        return null;
    }

    private bool IsLocked(string actorUri)
    {
        if (_actorBackfillLocks.TryGetValue(actorUri, out var lockedAt))
        {
            if (DateTime.UtcNow - lockedAt < LockTimeout)
                return true;
            
            _actorBackfillLocks.TryRemove(actorUri, out _);
        }
        return false;
    }

    private void LockActor(string actorUri)
    {
        _actorBackfillLocks[actorUri] = DateTime.UtcNow;
    }

    private void UnlockActor(string actorUri)
    {
        _actorBackfillLocks.TryRemove(actorUri, out _);
    }

    private async Task UpdateActorBackfillTimeAsync(Guid actorId)
    {
        var actor = await db.FediverseActors.FindAsync(actorId);
        if (actor != null)
        {
            actor.OutboxFetchedAt = clock.GetCurrentInstant();
            await db.SaveChangesAsync();
        }
    }

    private static string? GetString(Dictionary<string, object>? dict, string key)
    {
        if (dict == null) return null;
        if (dict.TryGetValue(key, out var value))
        {
            return value switch
            {
                JsonElement element => element.GetString(),
                string str => str,
                _ => value?.ToString()
            };
        }
        return null;
    }

    private static Dictionary<string, object>? GetObject(Dictionary<string, object>? dict, string key)
    {
        if (dict == null) return null;
        if (dict.TryGetValue(key, out var value))
        {
            if (value is JsonElement element && element.ValueKind == JsonValueKind.Object)
            {
                return JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText());
            }
            if (value is Dictionary<string, object> obj)
            {
                return obj;
            }
        }
        return null;
    }
}
