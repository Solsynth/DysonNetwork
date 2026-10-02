using System.Text.Json;
using DysonNetwork.Sphere.Models;
using DysonNetwork.Shared.Models;
using DysonNetwork.Sphere.ActivityPub.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubDeliveryService(
    AppDatabase db,
    IActorDiscoveryService discoveryService,
    ActivityPubQueueService queueService,
    IConfiguration configuration,
    ILogger<ActivityPubDeliveryService> logger,
    ISignatureService signatureService,
    ActivityRenderer objFactory,
    FediverseCachingService cachingService,
    IHttpClientFactory httpClientFactory
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string AssetsBaseUrl =>
        configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

    public async Task<bool> SendAcceptActivityAsync(SnPublisher actor, string followerActorUri)
    {
        var actorUrl = actor.Uri;
        var followerActor = await db.FediverseActors.FirstOrDefaultAsync(a =>
            a.Uri == followerActorUri
        );

        if (followerActor?.InboxUri == null)
        {
            logger.LogWarning(
                "[Delivery] Follower actor or inbox not found: {Uri}",
                followerActorUri
            );
            return false;
        }

        var activityId = $"{actorUrl}/accepts/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Accept",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Follow",
                ["actor"] = followerActorUri,
                ["object"] = actorUrl,
            },
        };

        logger.LogInformation(
            "[Delivery] Sending Accept to {Inbox} from {Actor}",
            followerActor.InboxUri,
            actorUrl
        );
        return await EnqueueActivityDeliveryAsync(
            "Accept",
            activity,
            actorUrl,
            followerActor.InboxUri,
            activityId
        );
    }

    public async Task<bool> SendRejectActivityAsync(
        SnPublisher actor,
        string followerActorUri,
        string? reason = null
    )
    {
        var actorUrl = actor.Uri;
        var followerActor = await db.FediverseActors.FirstOrDefaultAsync(a =>
            a.Uri == followerActorUri
        );

        if (followerActor?.InboxUri == null)
        {
            logger.LogWarning(
                "[Delivery] Follower actor or inbox not found: {Uri}",
                followerActorUri
            );
            return false;
        }

        var activityId = $"{actorUrl}/rejects/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Reject",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Follow",
                ["actor"] = followerActorUri,
                ["object"] = actorUrl,
            },
        };

        if (!string.IsNullOrEmpty(reason))
            activity["summary"] = reason;

        logger.LogInformation(
            "[Delivery] Sending Reject to {Inbox} from {Actor}",
            followerActor.InboxUri,
            actorUrl
        );
        return await EnqueueActivityDeliveryAsync(
            "Reject",
            activity,
            actorUrl,
            followerActor.InboxUri,
            activityId
        );
    }

    public async Task<bool> SendFollowActivityAsync(Guid publisherId, string targetActorUri)
    {
        var localActor = await objFactory.GetLocalActorAsync(publisherId);
        if (localActor == null)
        {
            logger.LogWarning(
                "[Delivery] Local actor not found for publisher: {PublisherId}",
                publisherId
            );
            return false;
        }

        var actorUrl = localActor.Uri;
        var targetActor = await GetOrFetchActorAsync(targetActorUri);

        if (targetActor?.InboxUri == null)
        {
            logger.LogWarning("[Delivery] Target actor or inbox not found: {Uri}", targetActorUri);
            return false;
        }

        logger.LogInformation(
            "[Delivery] Sending Follow from {Actor} to {Target} inbox {Inbox}",
            actorUrl,
            targetActorUri,
            targetActor.InboxUri
        );

        var activityId = $"{actorUrl}/follows/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Follow",
            ["actor"] = actorUrl,
            ["object"] = targetActorUri,
        };

        var existingRelationship = await db.PublisherSubscriptions.FirstOrDefaultAsync(r =>
            r.FollowerPublisherId == localActor.Id && r.PublisherId == targetActor.Id
        );

        if (existingRelationship == null)
        {
            existingRelationship = new SnPublisherSubscription
            {
                AccountId = localActor.AccountId,
                FollowerPublisherId = localActor.Id,
                PublisherId = targetActor.Id,
                FollowedAt = SystemClock.Instance.GetCurrentInstant(),
                State = PublisherSubscriptionState.Pending,
            };
            db.PublisherSubscriptions.Add(existingRelationship);
        }
        else
        {
            existingRelationship.State = PublisherSubscriptionState.Pending;
            existingRelationship.EndedAt = null;
        }

        await db.SaveChangesAsync();

        await cachingService.InvalidateRelationshipAsync(localActor.Id, targetActor.Id);

        return await EnqueueActivityDeliveryAsync(
            "Follow",
            activity,
            actorUrl,
            targetActor.InboxUri,
            activityId
        );
    }

    public async Task<bool> SendUnfollowActivityAsync(Guid publisherId, string targetActorUri)
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
                ["object"] = targetActor.InboxUri,
            },
        };

        var relationship = await db.PublisherSubscriptions.FirstOrDefaultAsync(r =>
            r.FollowerPublisherId == localActor.Id && r.PublisherId == targetActor.Id
        );
        if (relationship == null)
            return false;

        var success = await EnqueueActivityDeliveryAsync(
            "Undo",
            activity,
            actorUrl,
            targetActor.InboxUri,
            activityId
        );

        db.Remove(relationship);
        await db.SaveChangesAsync();

        await cachingService.InvalidateRelationshipAsync(localActor.Id, targetActor.Id);

        return success;
    }

    public async Task<bool> SendCreateActivityAsync(SnPost post)
    {
        if (post.PublisherId == Guid.Empty)
            return false;
        var localActor = await objFactory.GetLocalActorAsync(post.PublisherId);
        if (localActor == null)
            return false;

        var actorUrl = localActor.Uri;
        var postUrl = $"https://{Domain}/posts/{post.Id}";
        var activityId = $"{postUrl}/activity";

        var postReceivers = new List<string> { $"{actorUrl}/followers" };

        if (post.RepliedPostId != null)
        {
            var repliedPost = await db
                .Posts.Where(p => p.Id == post.RepliedPostId)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();
            post.RepliedPost = repliedPost;

            var repliedActor = repliedPost != null
                ? await objFactory.GetLocalActorAsync(repliedPost.PublisherId)
                : null;
            if (repliedActor?.FollowersUri != null)
                postReceivers.Add(repliedActor.FollowersUri);
        }

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Create",
            ["actor"] = actorUrl,
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["to"] = new[] { ActivityRenderer.PublicTo },
            ["cc"] = postReceivers.ToArray(),
            ["object"] = await objFactory.CreatePostObject(post, actorUrl),
        };

        var followers = await GetRemoteFollowersAsync(localActor.Id);
        if (post.RepliedPost != null)
            followers.AddRange(await GetRemoteFollowersAsync(post.RepliedPost.PublisherId));

        logger.LogInformation("Enqueuing Create activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Create",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUpdateActivityAsync(SnPost post)
    {
        if (post.PublisherId == Guid.Empty)
            return false;
        var localActor = await objFactory.GetLocalActorAsync(post.PublisherId);
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
            ["to"] = new[] { ActivityRenderer.PublicTo },
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = await objFactory.CreatePostObject(post, actorUrl),
        };

        var followers = await GetRemoteFollowersAsync();
        logger.LogInformation("Enqueuing Update activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Update",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendDeleteActivityAsync(SnPost post)
    {
        if (post.PublisherId == Guid.Empty)
            return false;
        var localActor = await objFactory.GetLocalActorAsync(post.PublisherId);
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
                ["type"] = "Tombstone",
            },
        };

        var followers = await GetRemoteFollowersAsync();
        logger.LogInformation("Enqueuing Delete activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Delete",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUpdateActorActivityAsync(SnPublisher actor)
    {
        var publisher = actor;

        actor.DisplayName = publisher.Nick;
        actor.Bio = publisher.Bio;
        actor.Username = publisher.Name;
        actor.AvatarUrl =
            publisher.Picture != null ? $"{AssetsBaseUrl}/{publisher.Picture.Id}" : null;
        actor.HeaderUrl =
            publisher.Background != null ? $"{AssetsBaseUrl}/{publisher.Background.Id}" : null;
        actor.LastActivityAt = SystemClock.Instance.GetCurrentInstant();

        var actorUrl = actor.Uri;

        var actorObject = new Dictionary<string, object?>
        {
            ["id"] = actorUrl,
            ["type"] = actor.ActorType ?? "Person",
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
                ["publicKeyPem"] = actor.PublicKey,
            },
            ["manuallyApprovesFollowers"] = actor.IsLocked,
            ["discoverable"] = actor.IsDiscoverable,
            ["bot"] = actor.IsBot,
        };

        if (publisher.Picture != null)
        {
            actorObject["icon"] = new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["mediaType"] = publisher.Picture.MimeType,
                ["url"] = $"{AssetsBaseUrl}/{publisher.Picture.Id}",
            };
        }

        if (publisher.Background != null)
        {
            actorObject["image"] = new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["mediaType"] = publisher.Background.MimeType,
                ["url"] = $"{AssetsBaseUrl}/{publisher.Background.Id}",
            };
        }

        await db.SaveChangesAsync();

        var activityId = $"{actorUrl}#update-{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = new List<object>
            {
                "https://www.w3.org/ns/activitystreams",
                "https://w3id.org/security/v1",
            },
            ["id"] = activityId,
            ["type"] = "Update",
            ["actor"] = actorUrl,
            ["published"] = DateTimeOffset.UtcNow,
            ["to"] = Array.Empty<object>(),
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = actorObject,
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        logger.LogInformation(
            "Enqueuing Update actor activity for {Count} followers",
            followers.Count
        );

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Update",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendLikeActivityToLocalPostAsync(
        SnPublisher actor,
        Guid postId,
        SnPublisher postSenderActor
    )
    {
        var actorUrl = actor.Uri;
        var postUrl = $"https://{Domain}/posts/{postId}";
        var activityId = $"{actorUrl}/likes/{Guid.NewGuid()}";

        var post = await db.Posts.FindAsync(postId);
        if (post == null)
            return false;

        var postObject = await objFactory.CreatePostObject(post, actorUrl);

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Like",
            ["actor"] = actor.Uri,
            ["object"] = postObject,
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[]
            {
                $"{actorUrl}/followers",
                postSenderActor.Uri,
                postSenderActor.FollowersUri,
            },
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        var ogFollowers = await GetRemoteFollowersAsync(postSenderActor.Id);

        foreach (var follower in followers.Concat(ogFollowers))
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Like",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUndoLikeActivityAsync(
        SnPublisher actor,
        Guid postId,
        SnPublisher postSenderActor
    )
    {
        var actorUrl = actor.Uri;
        var postUrl = $"https://{Domain}/posts/{postId}";
        var activityId = $"{actorUrl}/undo/{Guid.NewGuid()}";

        var post = await db.Posts.FindAsync(postId);
        if (post == null)
            return false;

        var postObject = await objFactory.CreatePostObject(post, actorUrl);

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Like",
                ["object"] = postObject,
            },
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[]
            {
                $"{actorUrl}/followers",
                postSenderActor.Uri,
                postSenderActor.FollowersUri,
            },
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        var ogFollowers = await GetRemoteFollowersAsync(postSenderActor.Id);

        foreach (var follower in followers.Concat(ogFollowers))
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Undo",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendAnnounceActivityAsync(
        SnPost post,
        SnPublisher actor,
        string? content = null,
        string? quoteUri = null
    )
    {
        var actorUrl = actor.Uri;
        var postUrl = $"https://{Domain}/posts/{post.Id}";
        var activityId = $"{actorUrl}/announces/{Guid.NewGuid()}";

        var targetPostUri = post.FediverseUri ?? postUrl;
        var targetWebUrl =
            post.FediverseUri != null
                ? $"https://{new Uri(post.FediverseUri).Host}/@{new Uri(post.FediverseUri).Segments.ElementAtOrDefault(1)?.Trim('/')}/{new Uri(post.FediverseUri).Segments.LastOrDefault()}"
                : postUrl;

        var announceObject = new Dictionary<string, object>
        {
            ["id"] = targetPostUri,
            ["type"] = post.Type is PostType.Article or PostType.Blog ? "Article" : "Note",
        };

        if (!string.IsNullOrEmpty(content))
        {
            announceObject["content"] = content;
        }

        if (!string.IsNullOrEmpty(quoteUri))
        {
            announceObject["quoteUri"] = quoteUri;
        }

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Announce",
            ["actor"] = actorUrl,
            ["object"] = targetPostUri,
            ["url"] = targetWebUrl,
            ["to"] = new[] { ActivityRenderer.PublicTo },
            ["cc"] = new[] { $"{actorUrl}/followers" },
        };

        if (!string.IsNullOrEmpty(content))
        {
            activity["content"] = content;
        }

        if (!string.IsNullOrEmpty(quoteUri))
        {
            activity["quoteUri"] = quoteUri;
        }

        var followers = await GetRemoteFollowersAsync(actor.Id);
        logger.LogInformation("Enqueuing Announce activity for {Count} followers", followers.Count);

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Announce",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUndoAnnounceActivityAsync(SnPost post, SnPublisher actor)
    {
        var actorUrl = actor.Uri;
        var postUrl = $"https://{Domain}/posts/{post.Id}";
        var activityId = $"{actorUrl}/undo/{Guid.NewGuid()}";

        var targetPostUri = post.FediverseUri ?? postUrl;

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Announce",
                ["id"] = targetPostUri,
                ["actor"] = actorUrl,
            },
            ["to"] = new[] { ActivityRenderer.PublicTo },
            ["cc"] = new[] { $"{actorUrl}/followers" },
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        logger.LogInformation(
            "Enqueuing Undo Announce activity for {Count} followers",
            followers.Count
        );

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Undo",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendLikeActivityAsync(
        Guid postId,
        Guid accountId,
        string targetActorUri
    )
    {
        var publisher = await db
            .Publishers.Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == accountId))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return false;

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var postUrl = $"https://{Domain}/posts/{postId}";
        var targetActor = await GetOrFetchActorAsync(targetActorUri);

        if (targetActor?.InboxUri == null)
            return false;

        var post = await db.Posts.FindAsync(postId);
        if (post == null)
            return false;

        var postObject = await objFactory.CreatePostObject(post, actorUrl);

        var activityId = $"{actorUrl}/likes/{Guid.NewGuid()}";
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Like",
            ["actor"] = actorUrl,
            ["object"] = postObject,
        };

        return await EnqueueActivityDeliveryAsync(
            "Like",
            activity,
            actorUrl,
            targetActor.InboxUri,
            activityId
        );
    }

    public async Task<bool> SendEmojiReactionActivityAsync(
        SnPublisher actor,
        Guid postId,
        string emoji,
        SnPublisher postSenderActor,
        string? activityId = null
    )
    {
        var actorUrl = actor.Uri;
        activityId ??= $"{actorUrl}/reactions/{Guid.NewGuid()}";

        var post = await db.Posts.FindAsync(postId);
        if (post == null)
            return false;

        var postObject = await objFactory.CreatePostObject(post, actorUrl);

        var context = new Dictionary<string, object>
        {
            ["@context"] = new Dictionary<string, object>
            {
                ["litepub"] = "http://litepub.social/ns#",
                ["EmojiReact"] = "litepub:EmojiReact",
            },
        };

        var activity = new Dictionary<string, object>
        {
            ["@context"] = context,
            ["id"] = activityId,
            ["type"] = "EmojiReact",
            ["actor"] = actorUrl,
            ["content"] = emoji,
            ["object"] = postObject,
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[]
            {
                $"{actorUrl}/followers",
                postSenderActor.Uri,
                postSenderActor.FollowersUri,
            },
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        var ogFollowers = await GetRemoteFollowersAsync(postSenderActor.Id);

        foreach (var follower in followers.Concat(ogFollowers))
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "EmojiReact",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<bool> SendUndoEmojiReactionActivityAsync(
        SnPublisher actor,
        Guid postId,
        string emoji,
        SnPublisher postSenderActor,
        string reactionActivityId
    )
    {
        var actorUrl = actor.Uri;
        var activityId = $"{actorUrl}/undo/{Guid.NewGuid()}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = activityId,
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "EmojiReact",
                ["content"] = emoji,
                ["object"] = $"https://{Domain}/posts/{postId}",
                ["id"] = reactionActivityId,
            },
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[]
            {
                $"{actorUrl}/followers",
                postSenderActor.Uri,
                postSenderActor.FollowersUri,
            },
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);
        var ogFollowers = await GetRemoteFollowersAsync(postSenderActor.Id);

        foreach (var follower in followers.Concat(ogFollowers))
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Undo",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
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
                ["object"] = objectUri,
            },
        };

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null)
                continue;
            await EnqueueActivityDeliveryAsync(
                "Undo",
                activity,
                actorUrl,
                follower.InboxUri,
                activityId
            );
        }

        return followers.Count > 0;
    }

    public async Task<List<SnActivityPubDelivery>> GetDeliveriesByActivityIdAsync(string activityId)
    {
        return await db
            .ActivityPubDeliveries.Where(d => d.ActivityId == activityId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<DeliveryStats> GetDeliveryStatsAsync(DateTimeOffset from, DateTimeOffset to)
    {
        var fromInstant = Instant.FromDateTimeOffset(from);
        var toInstant = Instant.FromDateTimeOffset(to);

        var stats = new DeliveryStats { From = from, To = to };

        var deliveries = await db
            .ActivityPubDeliveries.Where(d =>
                d.CreatedAt >= fromInstant && d.CreatedAt <= toInstant
            )
            .ToListAsync();

        stats.TotalDeliveries = deliveries.Count;
        stats.SentDeliveries = deliveries.Count(d => d.Status == DeliveryStatus.Sent);
        stats.FailedDeliveries = deliveries.Count(d =>
            d.Status == DeliveryStatus.Failed || d.Status == DeliveryStatus.ExhaustedRetries
        );
        stats.PendingDeliveries = deliveries.Count(d =>
            d.Status == DeliveryStatus.Pending || d.Status == DeliveryStatus.Processing
        );

        return stats;
    }

    public async Task<bool> EnqueueActivityDeliveryAsync(
        string activityType,
        Dictionary<string, object> activity,
        string actorUri,
        string inboxUri,
        string? activityId = null
    )
    {
        try
        {
            activityId ??= activity.TryGetValue("id", out var value)
                ? value?.ToString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();

            var delivery = new SnActivityPubDelivery
            {
                ActivityId = activityId,
                ActivityType = activityType,
                InboxUri = inboxUri,
                ActorUri = actorUri,
                Status = DeliveryStatus.Pending,
                RetryCount = 0,
                ActivityPayload = JsonSerializer.Serialize(activity)
            };

            db.ActivityPubDeliveries.Add(delivery);

            foreach (var entry in db.ChangeTracker.Entries<SnPublisher>().ToList())
            {
                entry.State = EntityState.Detached;
            }

            await db.SaveChangesAsync();
            
            // Attempt direct delivery immediately first
            try
            {
                delivery.Status = DeliveryStatus.Processing;
                delivery.LastAttemptAt = SystemClock.Instance.GetCurrentInstant();
                await db.SaveChangesAsync();
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var response = await ActivityPubDeliveryWorker.SendActivityToInboxAsync(
                    activity,
                    inboxUri,
                    actorUri,
                    signatureService,
                    httpClientFactory,
                    logger,
                    cts.Token
                );

                if (response.IsSuccessStatusCode)
                {
                    delivery.Status = DeliveryStatus.Sent;
                    delivery.SentAt = SystemClock.Instance.GetCurrentInstant();
                    delivery.ResponseStatusCode = response.StatusCode.ToString();
                    await db.SaveChangesAsync();
                    
                    logger.LogInformation(
                        "[Delivery] Successfully delivered {ActivityType} {ActivityId} to {Inbox}. Status: {Status}",
                        activityType,
                        activityId,
                        inboxUri,
                        response.StatusCode
                    );
                    
                    return true;
                }
                else
                {
                    var retryAfter = response.Headers.TryGetValues("Retry-After", out var values) && 
                                     values.FirstOrDefault() is { } ra && 
                                     double.TryParse(ra, out var seconds) 
                        ? TimeSpan.FromSeconds(seconds) 
                        : (TimeSpan?)null;
                    
                    var shouldRetry = ActivityPubDeliveryWorker.ShouldRetry(response.StatusCode, retryAfter);
                    delivery.ResponseStatusCode = response.StatusCode.ToString();
                    delivery.ErrorMessage = await response.Content.ReadAsStringAsync(cts.Token);
                    
                    if (shouldRetry && delivery.RetryCount < 5)
                    {
                        delivery.Status = DeliveryStatus.Failed;
                        delivery.RetryCount++;
                        delivery.NextRetryAt = ActivityPubDeliveryWorker.CalculateNextRetryAt(delivery.RetryCount, SystemClock.Instance, retryAfter);
                        
                        var message = new ActivityPubDeliveryMessage
                        {
                            DeliveryId = delivery.Id,
                            ActivityId = activityId,
                            ActivityType = activityType,
                            Activity = activity,
                            ActorUri = actorUri,
                            InboxUri = inboxUri,
                            CurrentRetry = 1
                        };

                        await queueService.EnqueueDeliveryAsync(message);
                        
                        logger.LogWarning(
                            "[Delivery] Failed delivery {ActivityType} {ActivityId} to {Inbox}. Status: {Status}. Enqueued for retry",
                            activityType,
                            activityId,
                            inboxUri,
                            response.StatusCode
                        );
                    }
                    else
                    {
                        delivery.Status = DeliveryStatus.ExhaustedRetries;
                        logger.LogError(
                            "[Delivery] Exhausted retries for {ActivityType} {ActivityId} to {Inbox}. Status: {Status}",
                            activityType,
                            activityId,
                            inboxUri,
                            response.StatusCode
                        );
                    }
                    
                    await db.SaveChangesAsync();
                    return true;
                }
            }
            catch (Exception deliveryEx)
            {
                delivery.Status = DeliveryStatus.Failed;
                delivery.ErrorMessage = deliveryEx.Message;
                delivery.RetryCount++;
                delivery.NextRetryAt = ActivityPubDeliveryWorker.CalculateNextRetryAt(delivery.RetryCount, SystemClock.Instance);
                await db.SaveChangesAsync();
                
                var message = new ActivityPubDeliveryMessage
                {
                    DeliveryId = delivery.Id,
                    ActivityId = activityId,
                    ActivityType = activityType,
                    Activity = activity,
                    ActorUri = actorUri,
                    InboxUri = inboxUri,
                    CurrentRetry = 1
                };

                await queueService.EnqueueDeliveryAsync(message);
                
                logger.LogWarning(deliveryEx, 
                    "[Delivery] Exception during direct delivery {ActivityId} to {Inbox}. Enqueued for retry",
                    activityId,
                    inboxUri
                );
                
                return true;
            }
        }
        catch (Exception ex)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogError(ex, "[Delivery] Failed to enqueue delivery to {Inbox}", inboxUri);
            }
            return false;
        }
    }

    private async Task<List<SnPublisher>> GetRemoteFollowersAsync()
    {
        var localActorIds = await db
            .Publishers.Where(a => a.Type != PublisherType.Fediverse)
            .Select(a => a.Id)
            .ToListAsync();

        return await db
            .PublisherSubscriptions.Include(r => r.FollowerPublisher)
            .Where(r =>
                r.State == PublisherSubscriptionState.Accepted
                && r.EndedAt == null
                && !r.IsBlocking
                && r.FollowerPublisherId != null
                && localActorIds.Contains(r.PublisherId)
            )
            .Select(r => r.FollowerPublisher!)
            .ToListAsync();
    }

    private async Task<List<SnPublisher>> GetRemoteFollowersAsync(Guid actorId)
    {
        return await db
            .PublisherSubscriptions.Include(r => r.FollowerPublisher)
            .Where(r => r.PublisherId == actorId && r.State == PublisherSubscriptionState.Accepted && r.EndedAt == null && !r.IsBlocking)
            .Select(r => r.FollowerPublisher!)
            .ToListAsync();
    }

    public async Task<SnPublisher?> GetOrCreateLocalActorAsync(SnPublisher publisher)
    {
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";

        if (!string.IsNullOrEmpty(publisher.Uri))
            return publisher;

        var instance = await db.FediverseInstances.FirstOrDefaultAsync(i => i.Domain == Domain);

        if (instance == null)
        {
            instance = new SnFediverseInstance { Domain = Domain, Name = Domain };
            db.FediverseInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        var assetsBaseUrl = configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

        publisher.Uri = actorUrl;
        publisher.ActorType ??= "Person";
        publisher.Username ??= publisher.Name;
        publisher.Nick = string.IsNullOrEmpty(publisher.Nick) ? publisher.Name : publisher.Nick;
        publisher.InboxUri = $"{actorUrl}/inbox";
        publisher.OutboxUri = $"{actorUrl}/outbox";
        publisher.FollowersUri = $"{actorUrl}/followers";
        publisher.FollowingUri = $"{actorUrl}/following";
        publisher.AvatarUrl ??=
            publisher.Picture != null ? $"{assetsBaseUrl}/{publisher.Picture.Id}" : null;
        publisher.HeaderUrl ??=
            publisher.Background != null ? $"{assetsBaseUrl}/{publisher.Background.Id}" : null;
        publisher.InstanceId = instance.Id;
        publisher.InstanceDomain = instance.Domain;

        try
        {
            await db.SaveChangesAsync();
            return publisher;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            logger.LogInformation(
                "Actor was created by another request, fetching: {ActorUri}",
                actorUrl
            );
            return await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == actorUrl);
        }
    }

    private async Task<SnPublisher?> GetOrFetchActorAsync(string actorUri)
    {
        var actor = await db
            .Publishers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor != null)
        {
            if (actor.DeletedAt != null)
            {
                actor.DeletedAt = null;
                await db.SaveChangesAsync();
            }
            return actor;
        }

        try
        {
            var domain = new Uri(actorUri).Host;
            var instance = await db.FediverseInstances.FirstOrDefaultAsync(i => i.Domain == domain);

            if (instance == null)
            {
                instance = new SnFediverseInstance { Domain = domain, Name = domain };
                db.FediverseInstances.Add(instance);
                await db.SaveChangesAsync();
            }

            actor = await discoveryService.GetOrCreateActorWithDataAsync(
                actorUri,
                ExtractUsername(actorUri),
                instance.Id
            );
            actor.InstanceDomain = instance.Domain;
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

