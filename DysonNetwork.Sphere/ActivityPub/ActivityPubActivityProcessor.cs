using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.Json;
using DysonNetwork.Shared.Proto;
using ContentMention = DysonNetwork.Shared.Models.ContentMention;
using PostContentType = DysonNetwork.Shared.Models.PostContentType;
using PostReactionAttitude = DysonNetwork.Shared.Models.PostReactionAttitude;
using PostType = DysonNetwork.Shared.Models.PostType;
using PostVisibility = DysonNetwork.Shared.Models.PostVisibility;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubActivityProcessor(
    AppDatabase db,
    ActivityPubSignatureService signatureService,
    ActivityPubDeliveryService deliveryService,
    ActivityPubDiscoveryService discoveryService,
    ILogger<ActivityPubActivityProcessor> logger
)
{
    public async Task<bool> ProcessIncomingActivityAsync(
        HttpContext context,
        string username,
        Dictionary<string, object> activity
    )
    {
        logger.LogInformation("Incoming activity request. Username: {Username}, Path: {Path}", 
            username, context.Request.Path);
        
        var activityType = activity.GetValueOrDefault("type")?.ToString();
        var activityId = activity.GetValueOrDefault("id")?.ToString();
        var actor = activity.GetValueOrDefault("actor")?.ToString();
        
        logger.LogInformation("Activity details - Type: {Type}, ID: {Id}, Actor: {Actor}", 
            activityType, activityId, actor);
        
        if (!signatureService.VerifyIncomingRequest(context, out var actorUri))
        {
            logger.LogWarning("Failed to verify signature for incoming activity. Type: {Type}, From: {Actor}", 
                activityType, actor);
            return false;
        }
        
        if (string.IsNullOrEmpty(actorUri))
            return false;
        
        logger.LogInformation("Signature verified successfully. Processing {Type} from {ActorUri}", 
            activityType, actorUri);
        
        switch (activityType)
        {
            case "Follow":
                return await ProcessFollowAsync(actorUri, activity);
            case "Accept":
                return await ProcessAcceptAsync(actorUri, activity);
            case "Reject":
                return await ProcessRejectAsync(actorUri, activity);
            case "Undo":
                return await ProcessUndoAsync(actorUri, activity);
            case "Create":
                return await ProcessCreateAsync(actorUri, activity);
            case "Like":
                return await ProcessLikeAsync(actorUri, activity);
            case "Announce":
                return await ProcessAnnounceAsync(actorUri, activity);
            case "Delete":
                return await ProcessDeleteAsync(actorUri, activity);
            case "Update":
                return await ProcessUpdateAsync(actorUri, activity);
            default:
                logger.LogWarning("Unsupported activity type: {Type}. Full activity: {Activity}", 
                    activityType, JsonSerializer.Serialize(activity));
                return false;
        }
    }

    private async Task<bool> ProcessFollowAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        var activityId = activity.GetValueOrDefault("id")?.ToString();
        
        logger.LogInformation("Processing Follow. Actor: {ActorUri}, Target: {ObjectUri}, ActivityId: {Id}", 
            actorUri, objectUri, activityId);
        
        if (string.IsNullOrEmpty(objectUri))
        {
            logger.LogWarning("Follow activity missing object field");
            return false;
        }
        
        var actor = await GetOrCreateActorAsync(actorUri);
        var targetUsername = ExtractUsernameFromUri(objectUri);
        var targetPublisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name == targetUsername);
        
        if (targetPublisher == null)
        {
            logger.LogWarning("Target publisher not found: {Uri}, ExtractedUsername: {Username}",
                objectUri, targetUsername);
            return false;
        }

        var localActor = await deliveryService.GetLocalActorAsync(targetPublisher.Id);
        if (localActor == null)
        {
            logger.LogWarning("Target publisher has no enabled fediverse actor");
            return false;
        }
        
        logger.LogInformation("Target publisher found: {PublisherName} (ID: {Id})", 
            targetPublisher.Name, targetPublisher.Id);
        
        var existingRelationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r =>
                r.ActorId == actor.Id &&
                r.TargetActorId == localActor.Id);
        
        if (existingRelationship is { State: RelationshipState.Accepted })
        {
            logger.LogInformation("Follow relationship already exists and is accepted. ActorId: {ActorId}, PublisherId: {PublisherId}", 
                actor.Id, targetPublisher.Id);
            return true;
        }
        
        if (existingRelationship == null)
        {
            existingRelationship = new SnFediverseRelationship
            {
                ActorId = actor.Id,
                TargetActorId = localActor.Id,
                State = RelationshipState.Pending,
                IsFollowing = false,
                IsFollowedBy = true
            };
            db.FediverseRelationships.Add(existingRelationship);
            logger.LogInformation("Created new follow relationship. ActorId: {ActorId}, TargetActorId: {TargetActorId}", 
                actor.Id, actor.Id);
        }
        else
        {
            logger.LogInformation("Updating existing relationship. CurrentState: {State}, NewState: Pending", 
                existingRelationship.State);
        }
        
        await db.SaveChangesAsync();
        
        await deliveryService.SendAcceptActivityAsync(
            targetPublisher.Id,
            actorUri,
            activityId ?? ""
        );
        
        logger.LogInformation("Processed follow from {Actor} to {Target}. RelationshipState: Pending", 
            actorUri, objectUri);
        return true;
    }

    private async Task<bool> ProcessAcceptAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;
        
        var actor = await GetOrCreateActorAsync(actorUri);
        
        var relationship = await db.FediverseRelationships
            .Include(r => r.Actor)
            .Include(r => r.TargetActor)
            .FirstOrDefaultAsync(r =>
                r.TargetActorId == actor.Id);

        if (relationship == null)
        {
            // Assume objectUri is the local actor URI that was followed
            var localActor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == objectUri);
            if (localActor == null)
            {
                logger.LogWarning("Local actor not found for accept object: {ObjectUri}", objectUri);
                return false;
            }
            relationship = new SnFediverseRelationship
            {
                ActorId = localActor.Id,
                TargetActorId = actor.Id,
                State = RelationshipState.Accepted,
                IsFollowing = true,
                IsFollowedBy = false,
                FollowedAt = SystemClock.Instance.GetCurrentInstant()
            };
            db.FediverseRelationships.Add(relationship);
        }
        else
        {
            relationship.State = RelationshipState.Accepted;
            relationship.IsFollowing = true;
            relationship.FollowedAt = SystemClock.Instance.GetCurrentInstant();
        }

        await db.SaveChangesAsync();
        
        logger.LogInformation("Processed accept from {Actor}", actorUri);
        return true;
    }

    private async Task<bool> ProcessRejectAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;
        
        var actor = await GetOrCreateActorAsync(actorUri);
        
        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => 
                r.TargetActorId == actor.Id);
        
        if (relationship == null)
        {
            logger.LogWarning("No relationship found for reject");
            return false;
        }
        
        relationship.State = RelationshipState.Rejected;
        relationship.IsFollowing = false;
        relationship.RejectReason = "Remote rejected follow";
        
        await db.SaveChangesAsync();
        
        logger.LogInformation("Processed reject from {Actor}", actorUri);
        return true;
    }

    private async Task<bool> ProcessUndoAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectValue = activity.GetValueOrDefault("object");
        if (objectValue == null)
            return false;

        if (objectValue is not Dictionary<string, object> objectDict) return false;
        var objectType = objectDict.GetValueOrDefault("type")?.ToString();
        switch (objectType)
        {
            case "Follow":
                return await UndoFollowAsync(actorUri, objectDict.GetValueOrDefault("id")?.ToString());
            case "Like":
                return await UndoLikeAsync(actorUri, objectDict.GetValueOrDefault("id")?.ToString());
            case "Announce":
                return await UndoAnnounceAsync(actorUri, objectDict.GetValueOrDefault("id")?.ToString());
            default:
                return false;
        }

        return false;
    }

    private async Task<bool> ProcessCreateAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectValue = activity.GetValueOrDefault("object");
        if (objectValue == null || !(objectValue is Dictionary<string, object> objectDict))
            return false;
        
        var objectType = objectDict.GetValueOrDefault("type")?.ToString();
        if (objectType != "Note" && objectType != "Article")
        {
            logger.LogInformation("Skipping non-note content type: {Type}", objectType);
            return true;
        }
        
        var actor = await GetOrCreateActorAsync(actorUri);
        
        var contentUri = objectDict.GetValueOrDefault("id")?.ToString();
        if (string.IsNullOrEmpty(contentUri))
            return false;
        
        var existingContent = await db.Posts
            .FirstOrDefaultAsync(c => c.FediverseUri == contentUri);
        
        if (existingContent != null)
        {
            logger.LogInformation("Content already exists: {Uri}", contentUri);
            return true;
        }
        
        var content = new SnPost
        {
            FediverseUri = contentUri,
            FediverseType = objectType == "Article" ? FediverseContentType.FediverseArticle : FediverseContentType.FediverseNote,
            Title = objectDict.GetValueOrDefault("name")?.ToString(),
            Description = objectDict.GetValueOrDefault("summary")?.ToString(),
            Content = objectDict.GetValueOrDefault("content")?.ToString(),
            ContentType = objectDict.GetValueOrDefault("contentMap") != null ? PostContentType.Html : PostContentType.Markdown,
            PublishedAt = ParseInstant(objectDict.GetValueOrDefault("published")),
            EditedAt = ParseInstant(objectDict.GetValueOrDefault("updated")),
            ActorId = actor.Id,
            Language = objectDict.GetValueOrDefault("language")?.ToString(),
            Mentions = ParseMentions(objectDict.GetValueOrDefault("tag")),
            Attachments = ParseAttachments(objectDict.GetValueOrDefault("attachment")) ?? [],
            Type = objectType == "Article" ? PostType.Article : PostType.Moment,
            Visibility = PostVisibility.Public,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UpdatedAt = SystemClock.Instance.GetCurrentInstant(),
            Metadata = BuildMetadataFromActivity(objectDict)
        };
        
        db.Posts.Add(content);
        await db.SaveChangesAsync();
        
        logger.LogInformation("Created federated content: {Uri}", contentUri);
        return true;
    }

    private async Task<bool> ProcessLikeAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;
        
        var actor = await GetOrCreateActorAsync(actorUri);
        var content = await db.Posts
            .FirstOrDefaultAsync(c => c.FediverseUri == objectUri);
        
        if (content == null)
        {
            logger.LogWarning("Content not found for like: {Uri}", objectUri);
            return false;
        }
        
        var existingReaction = await db.PostReactions
            .FirstOrDefaultAsync(r =>
                r.ActorId == actor.Id &&
                r.PostId == content.Id &&
                r.Symbol == "❤️");
        
        if (existingReaction != null)
        {
            logger.LogInformation("Like already exists");
            return true;
        }
        
        var reaction = new SnPostReaction
        {
            FediverseUri = activity.GetValueOrDefault("id")?.ToString() ?? Guid.NewGuid().ToString(),
            Symbol = "❤️",
            Attitude = PostReactionAttitude.Positive,
            IsLocal = false,
            PostId = content.Id,
            ActorId = actor.Id,
            Actor = actor,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        
        db.PostReactions.Add(reaction);
        content.LikeCount++;
        
        await db.SaveChangesAsync();
        
        logger.LogInformation("Processed like from {Actor}", actorUri);
        return true;
    }

    private async Task<bool> ProcessAnnounceAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;
        
        var actor = await GetOrCreateActorAsync(actorUri);
        var content = await db.Posts
            .FirstOrDefaultAsync(c => c.FediverseUri == objectUri);
        
        if (content != null)
        {
            content.BoostCount++;
            await db.SaveChangesAsync();
        }
        
        logger.LogInformation("Processed announce from {Actor}", actorUri);
        return true;
    }

    private async Task<bool> ProcessDeleteAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;
        
        var content = await db.Posts
            .FirstOrDefaultAsync(c => c.FediverseUri == objectUri);
        
        if (content != null)
        {
            content.DeletedAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
            logger.LogInformation("Deleted federated content: {Uri}", objectUri);
        }
        
        return true;
    }

    private async Task<bool> ProcessUpdateAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;
        
        var content = await db.Posts
            .FirstOrDefaultAsync(c => c.FediverseUri == objectUri);
        
        if (content != null)
        {
            content.EditedAt = SystemClock.Instance.GetCurrentInstant();
            content.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
            logger.LogInformation("Updated federated content: {Uri}", objectUri);
        }
        
        return true;
    }

    private async Task<bool> UndoFollowAsync(string actorUri, string? activityId)
    {
        var actor = await GetOrCreateActorAsync(actorUri);
        
        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => 
                r.ActorId == actor.Id || 
                r.TargetActorId == actor.Id);
        
        if (relationship != null)
        {
            relationship.IsFollowing = false;
            relationship.IsFollowedBy = false;
            await db.SaveChangesAsync();
            logger.LogInformation("Undid follow relationship");
        }
        
        return true;
    }

    private async Task<bool> UndoLikeAsync(string actorUri, string? activityId)
    {
        var actor = await GetOrCreateActorAsync(actorUri);
        
        var reactions = await db.PostReactions
            .Where(r => r.ActorId == actor.Id && r.Symbol == "❤️")
            .ToListAsync();
        
        foreach (var reaction in reactions)
        {
            var content = await db.Posts.FindAsync(reaction.PostId);
            if (content != null)
            {
                content.LikeCount--;
            }
            db.PostReactions.Remove(reaction);
        }
        
        await db.SaveChangesAsync();
        return true;
    }

    private async Task<bool> UndoAnnounceAsync(string actorUri, string? activityId)
    {
        var content = await db.Posts
            .FirstOrDefaultAsync(c => c.FediverseUri == activityId);
        
        if (content != null)
        {
            content.BoostCount = Math.Max(0, content.BoostCount - 1);
            await db.SaveChangesAsync();
        }
        
        return true;
    }

    private async Task<SnFediverseActor> GetOrCreateActorAsync(string actorUri)
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUri);
        
        if (actor == null)
        {
            var instance = await GetOrCreateInstanceAsync(actorUri);
            actor = new SnFediverseActor
            {
                Uri = actorUri,
                Username = ExtractUsernameFromUri(actorUri),
                DisplayName = ExtractUsernameFromUri(actorUri),
                InstanceId = instance.Id
            };
            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();
            
            await discoveryService.FetchActorDataAsync(actor);
        }
        
        return actor;
    }

    private async Task<SnFediverseInstance> GetOrCreateInstanceAsync(string actorUri)
    {
        var domain = ExtractDomainFromUri(actorUri);
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
            await discoveryService.FetchInstanceMetadataAsync(instance);
        }
        
        return instance;
    }

    private string ExtractUsernameFromUri(string uri)
    {
        return uri.Split('/').Last();
    }

    private string ExtractDomainFromUri(string uri)
    {
        var uriObj = new Uri(uri);
        return uriObj.Host;
    }

    private Instant? ParseInstant(object? value)
    {
        if (value == null)
            return null;
        
        if (value is Instant instant)
            return instant;
        
        if (DateTimeOffset.TryParse(value.ToString(), out var dateTimeOffset))
            return Instant.FromDateTimeOffset(dateTimeOffset);
        
        return null;
    }

    private static List<SnCloudFileReferenceObject>? ParseAttachments(object? value)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray()
                .Select(attachment => new SnCloudFileReferenceObject
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = attachment.GetProperty("name").GetString() ?? string.Empty,
                    Url = attachment.GetProperty("url").GetString(),
                    MimeType = attachment.GetProperty("mediaType").GetString(),
                    Width = attachment.GetProperty("width").GetInt32(),
                    Height = attachment.GetProperty("height").GetInt32(),
                    Blurhash = attachment.GetProperty("blurhash").GetString(),
                    FileMeta = new Dictionary<string, object?>(),
                    UserMeta = new Dictionary<string, object?>(),
                    Size = 0,
                    CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                    UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                })
                .ToList();
        }

        return null;
    }

    private static List<ContentMention>? ParseMentions(object? value)
    {
        if (value is JsonElement { ValueKind: JsonValueKind.Array } element)
        {
            return element.EnumerateArray()
                .Where(e => e.GetProperty("type").GetString() == "Mention")
                .Select(mention => new ContentMention
                {
                    Username = mention.GetProperty("name").GetString(),
                    ActorUri = mention.GetProperty("href").GetString()
                })
                .ToList();
        }

        return null;
    }

    private static Dictionary<string, object> BuildMetadataFromActivity(Dictionary<string, object> objectDict)
    {
        var metadata = new Dictionary<string, object>();

        var tagsValue = objectDict.GetValueOrDefault("tag");
        if (tagsValue is JsonElement { ValueKind: JsonValueKind.Array } tagsElement)
        {
            var fediverseTags = tagsElement.EnumerateArray()
                .Where(e => e.GetProperty("type").GetString() == "Hashtag")
                .Select(e => e.GetProperty("name").GetString())
                .ToList();

            if (fediverseTags.Count > 0)
            {
                metadata["fediverseTags"] = fediverseTags;
            }
        }

        var emojiValue = objectDict.GetValueOrDefault("emoji");
        if (emojiValue is not JsonElement { ValueKind: JsonValueKind.Array } emojiElement) return metadata;
        {
            var emojis = emojiElement.EnumerateArray()
                .Select(e => new
                {
                    Shortcode = e.GetProperty("shortcode").GetString(),
                    StaticUrl = e.GetProperty("static_url").GetString(),
                    Url = e.GetProperty("url").GetString()
                })
                .ToList();

            if (emojis.Count > 0)
            {
                metadata["fediverseEmojis"] = emojis;
            }
        }

        return metadata;
    }
}
