using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubDeliveryService(
    AppDatabase db,
    ActivityPubDiscoveryService discoveryService,
    ActivityPubQueueService queueService,
    IConfiguration configuration,
    ILogger<ActivityPubDeliveryService> logger,
    IClock clock,
    ActivityPubObjectFactory objFactory
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string AssetsBaseUrl => configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

    public async Task<bool> SendAcceptActivityAsync(
        SnFediverseActor actor,
        string followerActorUri
    )
    {
        var actorUrl = actor.Uri;
        var followerActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == followerActorUri);

        if (followerActor?.InboxUri == null)
        {
            logger.LogWarning("Follower actor or inbox not found: {Uri}", followerActorUri);
            return false;
        }

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/accepts/{Guid.NewGuid()}",
            ["type"] = "Accept",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Follow",
                ["actor"] = followerActorUri,
                ["object"] = actorUrl
            }
        };

        return await EnqueueActivityDeliveryAsync("Accept", activity, actorUrl, followerActor.InboxUri);
    }

    public async Task<bool> SendFollowActivityAsync(
        Guid publisherId,
        string targetActorUri
    )
    {
        var localActor = await objFactory.GetLocalActorAsync(publisherId);
        if (localActor == null)
            return false;

        var actorUrl = localActor.Uri;
        var targetActor = await GetOrFetchActorAsync(targetActorUri);

        if (targetActor?.InboxUri == null)
        {
            logger.LogWarning("Target actor or inbox not found: {Uri}", targetActorUri);
            return false;
        }

        var activityId = $"{actorUrl}/follows/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Follow",
            ["actor"] = actorUrl,
            ["object"] = targetActorUri
        };

        var existingRelationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r =>
                r.ActorId == localActor.Id &&
                r.TargetActorId == targetActor.Id);

        if (existingRelationship == null)
        {
            existingRelationship = new SnFediverseRelationship
            {
                ActorId = localActor.Id,
                TargetActorId = targetActor.Id,
                State = RelationshipState.Pending
            };
            db.FediverseRelationships.Add(existingRelationship);
        }
        else
        {
            existingRelationship.State = RelationshipState.Pending;
        }

        await db.SaveChangesAsync();

        return await EnqueueActivityDeliveryAsync("Follow", activity, actorUrl, targetActor.InboxUri, activityId);
    }

    public async Task<bool> SendUnfollowActivityAsync(
        Guid publisherId,
        string targetActorUri
    )
    {
        var localActor = await objFactory.GetLocalActorAsync(publisherId);
        if (localActor == null)
            return false;

        var actorUrl = localActor.Uri;
        var targetActor = await GetOrFetchActorAsync(targetActorUri);

        if (targetActor?.InboxUri == null)
        {
            logger.LogWarning("Target actor or inbox not found: {Uri}", targetActorUri);
            return false;
        }

        var activityId = $"{actorUrl}/undo/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Follow",
                ["object"] = targetActor.InboxUri
            }
        };

        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r =>
                r.ActorId == localActor.Id &&
                r.TargetActorId == targetActor.Id);
        if (relationship == null) return false;

        var success = await EnqueueActivityDeliveryAsync("Undo", activity, actorUrl, targetActor.InboxUri, activityId);

        db.Remove(relationship);
        await db.SaveChangesAsync();

        return success;
    }

    public async Task<bool> SendCreateActivityAsync(SnPost post)
    {
        if (post.PublisherId == null)
            return false;
        var localActor = await objFactory.GetLocalActorAsync(post.PublisherId.Value);
        if (localActor == null)
            return false;

        var actorUrl = localActor.Uri;
        var postUrl = $"https://{Domain}/posts/{post.Id}";
        var activityId = $"{postUrl}/activity";

        var postReceivers = new List<string> { $"{actorUrl}/followers" };

        if (post.RepliedPostId != null)
        {
            var repliedPost = await db.Posts
                .Where(p => p.Id == post.RepliedPostId)
                .Include(p => p.Publisher)
                .Include(p => p.Actor)
                .FirstOrDefaultAsync();
            post.RepliedPost = repliedPost;

            // Local post
            if (repliedPost?.Publisher != null)
            {
                var actor = await objFactory.GetLocalActorAsync(repliedPost.PublisherId!.Value);
                if (actor?.FollowersUri != null)
                    postReceivers.Add(actor.FollowersUri);
            }

            // Fediverse post
            if (repliedPost?.Actor?.FollowersUri != null)
                postReceivers.Add(post.Actor!.FollowersUri!);
        }

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Create",
            ["actor"] = actorUrl,
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["to"] = new[] { ActivityPubObjectFactory.PublicTo },
            ["cc"] = postReceivers.ToArray(),
            ["object"] = await objFactory.CreatePostObject(post, actorUrl)
        };

        var followers = await GetRemoteFollowersAsync(localActor.Id);
        if (post.RepliedPost != null)
        {
            if (post.RepliedPost.PublisherId.HasValue)
            {
                var repliedLocalActor = await objFactory.GetLocalActorAsync(post.RepliedPost.PublisherId.Value);
                if (repliedLocalActor != null)
                    followers.AddRange(await GetRemoteFollowersAsync(repliedLocalActor.Id));
            }

            if (post.RepliedPost.ActorId.HasValue)
                followers.AddRange(await GetRemoteFollowersAsync(post.RepliedPost.ActorId.Value));
        }

        logger.LogInformation("Enqueuing Create activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            await EnqueueActivityDeliveryAsync("Create", activity, actorUrl, follower.InboxUri, activityId);
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUpdateActivityAsync(SnPost post)
    {
        if (post.PublisherId == null)
            return false;
        var localActor = await objFactory.GetLocalActorAsync(post.PublisherId.Value);
        if (localActor == null)
            return false;

        var actorUrl = localActor.Uri;
        var postUrl = $"https://{Domain}/posts/{post.Id}";
        var activityId = $"{postUrl}/activity/{Guid.NewGuid()}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Update",
            ["actor"] = actorUrl,
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["to"] = new[] { ActivityPubObjectFactory.PublicTo },
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = await objFactory.CreatePostObject(post, actorUrl)
        };

        var followers = await GetRemoteFollowersAsync();
        logger.LogInformation("Enqueuing Update activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            await EnqueueActivityDeliveryAsync("Update", activity, actorUrl, follower.InboxUri, activityId);
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendDeleteActivityAsync(SnPost post)
    {
        if (post.PublisherId == null)
            return false;
        var localActor = await objFactory.GetLocalActorAsync(post.PublisherId.Value);
        if (localActor == null)
            return false;

        var actorUrl = localActor.Uri;
        var postUrl = $"https://{Domain}/posts/{post.Id}";
        var activityId = $"{postUrl}/delete/{Guid.NewGuid()}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Delete",
            ["actor"] = actorUrl,
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = new Dictionary<string, object>
            {
                ["id"] = postUrl,
                ["type"] = "Tombstone"
            }
        };

        var followers = await GetRemoteFollowersAsync();
        logger.LogInformation("Enqueuing Delete activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            await EnqueueActivityDeliveryAsync("Delete", activity, actorUrl, follower.InboxUri, activityId);
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUpdateActorActivityAsync(SnFediverseActor actor)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Id == actor.PublisherId);

        if (publisher == null)
            return false;

        var actorUrl = actor.Uri;

        var actorObject = new Dictionary<string, object?>
        {
            ["id"] = actorUrl,
            ["type"] = actor.Type,
            ["name"] = publisher.Nick,
            ["preferredUsername"] = publisher.Name,
            ["summary"] = publisher.Bio ?? "",
            ["published"] = publisher.CreatedAt.ToDateTimeOffset(),
            ["updated"] = publisher.UpdatedAt.ToDateTimeOffset(),
            ["inbox"] = actor.InboxUri,
            ["outbox"] = actor.OutboxUri,
            ["followers"] = actor.FollowersUri,
            ["following"] = actor.FollowingUri,
            ["publicKey"] = new Dictionary<string, object?>
            {
                ["id"] = actor.PublicKeyId,
                ["owner"] = actorUrl,
                ["publicKeyPem"] = actor.PublicKey
            }
        };

        if (publisher.Picture != null)
        {
            actorObject["icon"] = new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["mediaType"] = publisher.Picture.MimeType,
                ["url"] = $"{AssetsBaseUrl}/{publisher.Picture.Id}"
            };
        }

        if (publisher.Background != null)
        {
            actorObject["image"] = new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["mediaType"] = publisher.Background.MimeType,
                ["url"] = $"{AssetsBaseUrl}/{publisher.Background.Id}"
            };
        }

        var activityId = $"{actorUrl}#update-{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = new List<object>
            {
                "https://www.w3.org/ns/activitystreams",
                "https://w3id.org/security/v1"
            },
            ["id"] = activityId,
            ["type"] = "Update",
            ["actor"] = actorUrl,
            ["published"] = DateTimeOffset.UtcNow,
            ["to"] = Array.Empty<object>(),
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = actorObject
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        logger.LogInformation("Enqueuing Update actor activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            await EnqueueActivityDeliveryAsync("Update", activity, actorUrl, follower.InboxUri, activityId);
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendLikeActivityToLocalPostAsync(
        SnFediverseActor actor,
        Guid postId,
        SnFediverseActor postSenderActor
    )
    {
        var actorUrl = actor.Uri;
        var postUrl = $"https://{Domain}/posts/{postId}";
        var activityId = $"{actorUrl}/likes/{Guid.NewGuid()}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Like",
            ["actor"] = actor.Uri,
            ["object"] = postUrl,
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers", postSenderActor.Uri, postSenderActor.FollowersUri }
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        var ogFollowers = await GetRemoteFollowersAsync(postSenderActor.Id);

        foreach (var follower in followers.Concat(ogFollowers))
        {
            if (follower.InboxUri == null) continue;
            await EnqueueActivityDeliveryAsync("Like", activity, actorUrl, follower.InboxUri, activityId);
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUndoLikeActivityAsync(
        SnFediverseActor actor,
        Guid postId,
        SnFediverseActor postSenderActor
    )
    {
        var actorUrl = actor.Uri;
        var postUrl = $"https://{Domain}/posts/{postId}";
        var activityId = $"{actorUrl}/undo/{Guid.NewGuid()}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Like",
                ["object"] = postUrl
            },
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers", postSenderActor.Uri, postSenderActor.FollowersUri }
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        var ogFollowers = await GetRemoteFollowersAsync(postSenderActor.Id);

        foreach (var follower in followers.Concat(ogFollowers))
        {
            if (follower.InboxUri == null) continue;
            await EnqueueActivityDeliveryAsync("Undo", activity, actorUrl, follower.InboxUri, activityId);
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendLikeActivityAsync(
        Guid postId,
        Guid accountId,
        string targetActorUri
    )
    {
        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == accountId))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return false;

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var postUrl = $"https://{Domain}/posts/{postId}";
        var targetActor = await GetOrFetchActorAsync(targetActorUri);

        if (targetActor?.InboxUri == null)
            return false;

        var activityId = $"{actorUrl}/likes/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Like",
            ["actor"] = actorUrl,
            ["object"] = postUrl
        };

        return await EnqueueActivityDeliveryAsync("Like", activity, actorUrl, targetActor.InboxUri, activityId);
    }

    public async Task<bool> SendUndoActivityAsync(
        string activityType,
        string objectUri,
        Guid publisherId
    )
    {
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return false;

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var followers = await GetRemoteFollowersAsync();

        var activityId = $"{actorUrl}/undo/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = activityType,
                ["object"] = objectUri
            }
        };

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            await EnqueueActivityDeliveryAsync("Undo", activity, actorUrl, follower.InboxUri, activityId);
        }

        return followers.Count > 0;
    }

    public async Task<List<SnActivityPubDelivery>> GetDeliveriesByActivityIdAsync(string activityId)
    {
        return await db.ActivityPubDeliveries
            .Where(d => d.ActivityId == activityId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<DeliveryStats> GetDeliveryStatsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var fromInstant = Instant.FromDateTimeOffset(from);
        var toInstant = Instant.FromDateTimeOffset(to);

        var stats = new DeliveryStats
        {
            From = from,
            To = to
        };

        var deliveries = await db.ActivityPubDeliveries
            .Where(d => d.CreatedAt >= fromInstant && d.CreatedAt <= toInstant)
            .ToListAsync();

        stats.TotalDeliveries = deliveries.Count;
        stats.SentDeliveries = deliveries.Count(d => d.Status == DeliveryStatus.Sent);
        stats.FailedDeliveries = deliveries.Count(d =>
            d.Status == DeliveryStatus.Failed || d.Status == DeliveryStatus.ExhaustedRetries);
        stats.PendingDeliveries =
            deliveries.Count(d => d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Processing);

        return stats;
    }

    private async Task<bool> EnqueueActivityDeliveryAsync(
        string activityType,
        Dictionary<string, object> activity,
        string actorUri,
        string inboxUri,
        string? activityId = null
    )
    {
        try
        {
            activityId ??= activity.ContainsKey("id") ? activity["id"].ToString() : Guid.NewGuid().ToString();

            var delivery = new SnActivityPubDelivery
            {
                ActivityId = activityId,
                ActivityType = activityType,
                InboxUri = inboxUri,
                ActorUri = actorUri,
                Status = DeliveryStatus.Pending,
                RetryCount = 0
            };

            db.ActivityPubDeliveries.Add(delivery);
            await db.SaveChangesAsync();

            var message = new ActivityPubDeliveryMessage
            {
                DeliveryId = delivery.Id,
                ActivityId = activityId,
                ActivityType = activityType,
                Activity = activity,
                ActorUri = actorUri,
                InboxUri = inboxUri,
                CurrentRetry = 0
            };

            await queueService.EnqueueDeliveryAsync(message);

            logger.LogDebug("Enqueued delivery {DeliveryId} of type {ActivityType} to {Inbox}",
                delivery.Id, activityType, inboxUri);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to enqueue delivery to {Inbox}", inboxUri);
            return false;
        }
    }

    private async Task<List<SnFediverseActor>> GetRemoteFollowersAsync()
    {
        var localActorIds = await db.FediverseActors
            .Where(a => a.PublisherId != null)
            .Select(a => a.Id)
            .ToListAsync();

        return await db.FediverseRelationships
            .Include(r => r.Actor)
            .Where(r => r.State == RelationshipState.Accepted && localActorIds.Contains(r.TargetActorId))
            .Select(r => r.Actor)
            .ToListAsync();
    }

    private async Task<List<SnFediverseActor>> GetRemoteFollowersAsync(Guid actorId)
    {
        return await db.FediverseRelationships
            .Include(r => r.Actor)
            .Where(r => r.TargetActorId == actorId && r.State == RelationshipState.Accepted)
            .Select(r => r.Actor)
            .ToListAsync();
    }

    public async Task<SnFediverseActor?> GetOrCreateLocalActorAsync(SnPublisher publisher)
    {
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";

        var localActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUrl);

        if (localActor != null)
            return localActor;

        var instance = await db.FediverseInstances
            .FirstOrDefaultAsync(i => i.Domain == Domain);

        if (instance == null)
        {
            instance = new SnFediverseInstance
            {
                Domain = Domain,
                Name = Domain
            };
            db.FediverseInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        var assetsBaseUrl = configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

        localActor = new SnFediverseActor
        {
            Uri = actorUrl,
            Username = publisher.Name,
            DisplayName = publisher.Name,
            Bio = publisher.Bio,
            InboxUri = $"{actorUrl}/inbox",
            OutboxUri = $"{actorUrl}/outbox",
            FollowersUri = $"{actorUrl}/followers",
            FollowingUri = $"{actorUrl}/following",
            AvatarUrl = publisher.Picture != null ? $"{assetsBaseUrl}/{publisher.Picture.Id}" : null,
            HeaderUrl = publisher.Background != null ? $"{assetsBaseUrl}/{publisher.Background.Id}" : null,
            InstanceId = instance.Id,
            PublisherId = publisher.Id,
        };

        db.FediverseActors.Add(localActor);
        await db.SaveChangesAsync();

        return localActor;
    }

    private async Task<SnFediverseActor?> GetOrFetchActorAsync(string actorUri)
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor != null)
            return actor;

        try
        {
            var domain = new Uri(actorUri).Host;
            var instance = await db.FediverseInstances
                .FirstOrDefaultAsync(i => i.Domain == domain);

            if (instance == null)
            {
                instance = new SnFediverseInstance
                {
                    Domain = domain,
                    Name = domain
                };
                db.FediverseInstances.Add(instance);
                await db.SaveChangesAsync();
            }

            actor = new SnFediverseActor
            {
                Uri = actorUri,
                Username = ExtractUsername(actorUri),
                InstanceId = instance.Id,
                LastFetchedAt = clock.GetCurrentInstant()
            };

            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();

            await discoveryService.FetchActorDataAsync(actor);
            await discoveryService.FetchInstanceMetadataAsync(instance);

            actor.Instance = instance;
            return actor;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch actor: {Uri}", actorUri);
            return null;
        }
    }

    private string ExtractUsername(string actorUri)
    {
        return actorUri.Split('/').Last();
    }
}

public class DeliveryStats
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int TotalDeliveries { get; set; }
    public int SentDeliveries { get; set; }
    public int FailedDeliveries { get; set; }
    public int PendingDeliveries { get; set; }
}