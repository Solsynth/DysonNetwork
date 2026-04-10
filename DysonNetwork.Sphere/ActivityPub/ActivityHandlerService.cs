using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PostContentType = DysonNetwork.Shared.Models.PostContentType;
using PostPinMode = DysonNetwork.Shared.Models.PostPinMode;
using PostReactionAttitude = DysonNetwork.Shared.Models.PostReactionAttitude;
using PostType = DysonNetwork.Shared.Models.PostType;
using PostVisibility = DysonNetwork.Shared.Models.PostVisibility;
using DyFediverseContentType = DysonNetwork.Shared.Proto.DyFediverseContentType;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityHandlerService(
    AppDatabase db,
    ISignatureService signatureService,
    ActivityPubDeliveryService deliveryService,
    IActorDiscoveryService discoveryService,
    FediverseModerationService moderationService,
    ILogger<ActivityHandlerService> logger,
    IConfiguration configuration
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string BaseUrl => $"https://{Domain}";

    public async Task<ActivityResult> ProcessActivityAsync(
        HttpContext context,
        string username,
        Dictionary<string, object> activity
    )
    {
        var activityType = GetString(activity, "type");
        var activityId = GetString(activity, "id");
        var objectUri = GetString(activity, "object");

        logger.LogInformation("[Inbox] Received activity. Type: {Type}, Id: {Id}, Object: {Object}", 
            activityType, activityId, objectUri);

        var (signatureValid, actorUri) = await signatureService.VerifyIncomingRequestAsync(context);
        if (!signatureValid || string.IsNullOrEmpty(actorUri))
        {
            logger.LogWarning("[Inbox] Signature verification failed for {Type} from {Actor}", 
                activityType, actorUri);
            return ActivityResult.Rejected;
        }

        logger.LogInformation("[Inbox] Signature verified for {Actor}", actorUri);

        var actorDomain = ExtractDomain(actorUri);
        var content = GetObjectContent(activity);
        var moderation = await moderationService.CheckActorAsync(actorUri, content, actorDomain);

        if (moderation.IsBlocked)
        {
            logger.LogWarning("[Inbox] Blocked activity from {Actor}. Rule: {Rule}", actorUri, moderation.MatchedRuleName);
            return ActivityResult.Rejected;
        }

        logger.LogInformation("[Inbox] Processing {Type} from {Actor}", activityType, actorUri);

        try
        {
            ActivityResult result;
            switch (activityType)
            {
                case "Follow":
                    result = await HandleFollowAsync(actorUri, activity);
                    break;
                case "Accept":
                    result = await HandleAcceptAsync(actorUri, activity);
                    break;
                case "Reject":
                    result = await HandleRejectAsync(actorUri, activity);
                    break;
                case "QuoteRequest":
                    result = await HandleQuoteRequestAsync(actorUri, activity);
                    break;
                case "Undo":
                    result = await HandleUndoAsync(actorUri, activity);
                    break;
                case "Create":
                    result = await HandleCreateAsync(actorUri, activity);
                    break;
                case "Like":
                case "EmojiReact":
                    result = await HandleLikeAsync(actorUri, activity);
                    break;
                case "Announce":
                    result = await HandleAnnounceAsync(actorUri, activity);
                    break;
                case "Delete":
                    result = await HandleDeleteAsync(actorUri, activity);
                    break;
                case "Update":
                    result = await HandleUpdateAsync(actorUri, activity);
                    break;
                case "Add":
                    result = await HandleAddAsync(actorUri, activity);
                    break;
                case "Remove":
                    result = await HandleRemoveAsync(actorUri, activity);
                    break;
                case "Block":
                    result = await HandleBlockAsync(actorUri, activity);
                    break;
                case "Move":
                    result = await HandleMoveAsync(actorUri, activity);
                    break;
                case "Flag":
                    result = await HandleFlagAsync(actorUri, activity);
                    break;
                default:
                    logger.LogWarning("[Inbox] Unsupported activity type: {Type}", activityType);
                    result = ActivityResult.NotSupported;
                    break;
            }

            logger.LogInformation("[Inbox] Processed {Type} from {Actor}. Result: {Result}", 
                activityType, actorUri, result);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Inbox] Error processing {Type} from {Actor}", activityType, actorUri);
            return ActivityResult.BadRequest;
        }
    }

    private async Task<ActivityResult> HandleFollowAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = GetString(activity, "object");
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var targetActor = await ResolveOrCreateActorAsync(objectUri);

        var existing = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => r.ActorId == actor.Id && r.TargetActorId == targetActor.Id);

        switch (existing?.State)
        {
            case RelationshipState.Accepted:
                logger.LogInformation("Follow already accepted: {Actor} -> {Target}", actorUri, objectUri);
                return ActivityResult.Success;
            case null:
                existing = new SnFediverseRelationship
                {
                    ActorId = actor.Id,
                    TargetActorId = targetActor.Id,
                    State = RelationshipState.Accepted
                };
                db.FediverseRelationships.Add(existing);
                logger.LogInformation("Created follow: {Actor} -> {Target}", actorUri, objectUri);
                break;
            default:
                existing.State = RelationshipState.Accepted;
                break;
        }

        await db.SaveChangesAsync();
        await deliveryService.SendAcceptActivityAsync(targetActor, actorUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleAcceptAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectDict = GetObject(activity, "object");
        if (objectDict == null)
            return ActivityResult.BadRequest;

        var objectType = GetString(objectDict, "type");

        if (objectType == "Follow")
            return await HandleFollowAcceptAsync(actorUri, objectDict);

        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleFollowAcceptAsync(string actorUri, Dictionary<string, object> objectDict)
    {
        var followActorUri = GetString(objectDict, "actor");
        var followObjectUri = GetString(objectDict, "object");

        if (string.IsNullOrEmpty(followObjectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);

        var relationship = await db.FediverseRelationships
            .Include(r => r.Actor)
            .Include(r => r.TargetActor)
            .FirstOrDefaultAsync(r => r.TargetActorId == actor.Id);

        if (relationship == null)
        {
            var localActor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == followObjectUri);
            if (localActor == null)
            {
                logger.LogWarning("Local actor not found: {Uri}", followObjectUri);
                return ActivityResult.NotFound;
            }

            relationship = new SnFediverseRelationship
            {
                ActorId = localActor.Id,
                TargetActorId = actor.Id,
                State = RelationshipState.Accepted
            };
            db.FediverseRelationships.Add(relationship);
        }
        else
        {
            relationship.State = RelationshipState.Accepted;
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Follow accepted by {Actor}", actorUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleRejectAsync(string actorUri, Dictionary<string, object> activity)
    {
        var actor = await ResolveOrCreateActorAsync(actorUri);

        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => r.TargetActorId == actor.Id);

        if (relationship != null)
        {
            relationship.State = RelationshipState.Rejected;
            relationship.RejectReason = "Remote rejected follow";
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Follow rejected by {Actor}", actorUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleQuoteRequestAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = GetString(activity, "object");
        var instrumentUri = GetString(activity, "instrument");

        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var targetPost = await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == objectUri);

        if (targetPost == null)
        {
            logger.LogWarning("QuoteRequest target not found: {Uri}", objectUri);
            return ActivityResult.NotFound;
        }

        var localActor = targetPost.PublisherId.HasValue
            ? await db.FediverseActors.FirstOrDefaultAsync(a => a.PublisherId == targetPost.PublisherId)
            : targetPost.Actor;

        if (localActor == null)
        {
            logger.LogWarning("Local author not found for quoted post");
            return ActivityResult.NotFound;
        }

        var auth = new SnQuoteAuthorization
        {
            FediverseUri = $"{BaseUrl}/quote-authorizations/{Guid.NewGuid()}",
            AuthorId = localActor.Id,
            InteractingObjectUri = instrumentUri ?? GetString(activity, "id") ?? "",
            InteractionTargetUri = objectUri,
            TargetPostId = targetPost.Id,
            IsValid = true
        };

        db.QuoteAuthorizations.Add(auth);
        await db.SaveChangesAsync();

        logger.LogInformation("QuoteRequest auto-approved for {Uri}", objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleUndoAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectDict = GetObject(activity, "object");
        if (objectDict == null)
            return ActivityResult.BadRequest;

        var objectType = GetString(objectDict, "type");
        var objectUri = GetString(objectDict, "object");

        return objectType switch
        {
            "Follow" => await UndoFollowAsync(actorUri, objectUri),
            "Like" => await UndoLikeAsync(actorUri, objectUri),
            "Announce" => await UndoAnnounceAsync(actorUri, objectUri),
            _ => ActivityResult.NotSupported
        };
    }

    private async Task<ActivityResult> UndoFollowAsync(string actorUri, string? objectUri)
    {
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var targetActor = await ResolveOrCreateActorAsync(objectUri);

        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => r.ActorId == actor.Id && r.TargetActorId == targetActor.Id);

        if (relationship != null)
        {
            db.FediverseRelationships.Remove(relationship);
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Follow undone: {Actor} -> {Target}", actorUri, objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> UndoLikeAsync(string actorUri, string? objectUri)
    {
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var post = await ResolvePostAsync(objectUri);

        if (post == null)
            return ActivityResult.NotFound;

        var reaction = await db.PostReactions
            .FirstOrDefaultAsync(r => r.ActorId == actor.Id && r.PostId == post.Id);

        if (reaction != null)
        {
            if (reaction.Attitude == PostReactionAttitude.Positive)
                post.Upvotes--;
            else if (reaction.Attitude == PostReactionAttitude.Negative)
                post.Downvotes--;

            db.PostReactions.Remove(reaction);
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Like undone: {Actor} on {Post}", actorUri, objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> UndoAnnounceAsync(string actorUri, string? objectUri)
    {
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var post = await ResolvePostAsync(objectUri);

        if (post == null)
        {
            logger.LogWarning("Post not found for undo announce: {Uri}", objectUri);
            return ActivityResult.NotFound;
        }

        var boost = await db.Boosts
            .FirstOrDefaultAsync(b => b.PostId == post.Id && b.ActorId == actor.Id);

        if (boost != null)
        {
            db.Boosts.Remove(boost);
            post.BoostCount = Math.Max(0, post.BoostCount - 1);
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Announce undone: {Actor} on {Post}", actorUri, objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleCreateAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectDict = GetObject(activity, "object");
        if (objectDict == null)
            return ActivityResult.BadRequest;

        var objectType = GetString(objectDict, "type");
        var objectId = GetString(objectDict, "id");

        if (objectType != "Note" && objectType != "Article")
        {
            logger.LogInformation("Skipping non-note type: {Type}", objectType);
            return ActivityResult.Success;
        }

        if (string.IsNullOrEmpty(objectId))
            return ActivityResult.BadRequest;

        var existing = await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == objectId);
        if (existing != null)
        {
            logger.LogInformation("Post already exists: {Uri}", objectId);
            return ActivityResult.Success;
        }

        var actor = await ResolveOrCreateActorAsync(actorUri);

        var post = new SnPost
        {
            FediverseUri = objectId,
            FediverseType = objectType == "Article"
                ? DyFediverseContentType.DyFediverseArticle
                : DyFediverseContentType.DyFediverseNote,
            Title = GetString(objectDict, "name"),
            Content = GetString(objectDict, "content"),
            ContentType = PostContentType.Html,
            PublishedAt = ParseInstant(GetValue(objectDict, "published")),
            EditedAt = ParseInstant(GetValue(objectDict, "updated")),
            ActorId = actor.Id,
            Language = GetString(objectDict, "language"),
            Type = objectType == "Article" ? PostType.Article : PostType.Moment,
            Visibility = PostVisibility.Public
        };

        var inReplyTo = GetString(objectDict, "inReplyTo");
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            var repliedTo = await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == inReplyTo);
            if (repliedTo != null)
                post.RepliedPostId = repliedTo.Id;
        }

        db.Posts.Add(post);
        await db.SaveChangesAsync();

        logger.LogInformation("Created federated post: {Uri}", objectId);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleLikeAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = GetString(activity, "object");
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var post = await ResolvePostAsync(objectUri);

        if (post == null)
        {
            logger.LogWarning("Post not found for like: {Uri}", objectUri);
            return ActivityResult.NotFound;
        }

        var existing = await db.PostReactions
            .FirstOrDefaultAsync(r => r.ActorId == actor.Id && r.PostId == post.Id);

        if (existing != null)
        {
            logger.LogInformation("Reaction already exists");
            return ActivityResult.Success;
        }

        var content = GetString(activity, "content");
        var (symbol, attitude) = GetReactionFromEmoji(content);

        var reaction = new SnPostReaction
        {
            FediverseUri = GetString(activity, "id") ?? Guid.NewGuid().ToString(),
            Symbol = symbol,
            Attitude = attitude,
            IsLocal = false,
            PostId = post.Id,
            ActorId = actor.Id,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.PostReactions.Add(reaction);
        if (attitude == PostReactionAttitude.Positive)
            post.Upvotes++;
        else if (attitude == PostReactionAttitude.Negative)
            post.Downvotes++;

        await db.SaveChangesAsync();

        logger.LogInformation("Like from {Actor} on {Post}", actorUri, objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleAnnounceAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = GetString(activity, "object");
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var post = await ResolvePostAsync(objectUri);

        if (post == null)
        {
            logger.LogInformation("Boosted post not found locally, fetching: {Uri}", objectUri);
            post = await FetchAndCreatePostAsync(objectUri, actorUri);
            if (post == null)
                return ActivityResult.NotFound;
        }

        var existing = await db.Boosts.FirstOrDefaultAsync(b => b.PostId == post.Id && b.ActorId == actor.Id);
        if (existing != null)
        {
            logger.LogDebug("Boost already exists");
            return ActivityResult.Success;
        }

        var boost = new SnBoost
        {
            PostId = post.Id,
            ActorId = actor.Id,
            ActivityPubUri = GetString(activity, "id"),
            BoostedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.Boosts.Add(boost);
        post.BoostCount++;
        await db.SaveChangesAsync();

        logger.LogInformation("Announce from {Actor}: {Uri}", actorUri, objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleDeleteAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = GetString(activity, "object");
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var post = await ResolvePostAsync(objectUri);
        if (post == null)
        {
            logger.LogInformation("Post not found for delete: {Uri}", objectUri);
            return ActivityResult.Success;
        }

        db.Posts.Remove(post);
        await db.SaveChangesAsync();

        logger.LogInformation("Deleted federated post: {Uri}", objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleUpdateAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectDict = GetObject(activity, "object");
        if (objectDict == null)
            return ActivityResult.BadRequest;

        var objectUri = GetString(objectDict, "id");
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var actor = await ResolveOrCreateActorAsync(actorUri);
        var post = await ResolvePostAsync(objectUri);

        if (post == null)
        {
            post = new SnPost
            {
                FediverseUri = objectUri,
                FediverseType = DyFediverseContentType.DyFediverseNote,
                Type = PostType.Moment,
                Visibility = PostVisibility.Public,
                ActorId = actor.Id
            };
            db.Posts.Add(post);
        }

        post.Title = GetString(objectDict, "name");
        post.Content = GetString(objectDict, "content");
        post.EditedAt = ParseInstant(GetValue(objectDict, "updated")) ?? SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();

        logger.LogInformation("Updated federated post: {Uri}", objectUri);
        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleAddAsync(string actorUri, Dictionary<string, object> activity)
    {
        var target = GetObject(activity, "target");
        if (target == null)
            return ActivityResult.BadRequest;

        var collection = GetString(target, "id");
        if (string.IsNullOrEmpty(collection) || !collection.Contains("/featured"))
            return ActivityResult.Success;

        var objectDict = GetObject(activity, "object");
        if (objectDict == null)
            return ActivityResult.BadRequest;

        var objectUri = GetString(objectDict, "id");
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var post = await ResolvePostAsync(objectUri);
        if (post == null)
            return ActivityResult.NotFound;

        if (post.PinMode != PostPinMode.PublisherPage)
        {
            post.PinMode = PostPinMode.PublisherPage;
            await db.SaveChangesAsync();
            logger.LogInformation("Pinned post {PostId} via Add activity", post.Id);
        }

        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleRemoveAsync(string actorUri, Dictionary<string, object> activity)
    {
        var target = GetObject(activity, "target");
        if (target == null)
            return ActivityResult.BadRequest;

        var collection = GetString(target, "id");
        if (string.IsNullOrEmpty(collection) || !collection.Contains("/featured"))
            return ActivityResult.Success;

        var objectDict = GetObject(activity, "object");
        if (objectDict == null)
            return ActivityResult.BadRequest;

        var objectUri = GetString(objectDict, "id");
        if (string.IsNullOrEmpty(objectUri))
            return ActivityResult.BadRequest;

        var post = await ResolvePostAsync(objectUri);
        if (post == null)
            return ActivityResult.Success;

        if (post.PinMode == PostPinMode.PublisherPage)
        {
            post.PinMode = (PostPinMode)0;
            await db.SaveChangesAsync();
            logger.LogInformation("Unpinned post {PostId} via Remove activity", post.Id);
        }

        return ActivityResult.Success;
    }

    private async Task<ActivityResult> HandleBlockAsync(string actorUri, Dictionary<string, object> activity)
    {
        var targetUri = GetString(activity, "object");
        if (string.IsNullOrEmpty(targetUri))
            return ActivityResult.BadRequest;

        var targetDomain = ExtractDomain(targetUri);

        var existingRule = await db.FediverseModerationRules
            .FirstOrDefaultAsync(r => r.Domain == targetDomain);

        if (existingRule == null)
        {
            existingRule = new SnFediverseModerationRule
            {
                Name = $"Block: {targetDomain}",
                Type = FediverseModerationRuleType.DomainBlock,
                Action = FediverseModerationAction.Block,
                Domain = targetDomain,
                IsEnabled = true
            };
            db.FediverseModerationRules.Add(existingRule);
            await db.SaveChangesAsync();
        }

        logger.LogInformation("Block from {Actor} targeting domain {Domain}", actorUri, targetDomain);
        return ActivityResult.Success;
    }

    private Task<ActivityResult> HandleMoveAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = GetString(activity, "object");
        var targetUri = GetString(activity, "target");

        logger.LogInformation("Move from {Actor}: {From} -> {To}", actorUri, objectUri, targetUri);
        return Task.FromResult(ActivityResult.Success);
    }

    private Task<ActivityResult> HandleFlagAsync(string actorUri, Dictionary<string, object> activity)
    {
        var content = GetString(activity, "content");
        logger.LogInformation("Flag from {Actor}: {Content}", actorUri, content);
        return Task.FromResult(ActivityResult.Success);
    }

    private async Task<SnFediverseActor> ResolveOrCreateActorAsync(string actorUri)
    {
        var domain = ExtractDomain(actorUri);
        var instance = await db.FediverseInstances.FirstOrDefaultAsync(i => i.Domain == domain);

        if (instance == null)
        {
            instance = new SnFediverseInstance { Domain = domain, Name = domain };
            db.FediverseInstances.Add(instance);
            await db.SaveChangesAsync();
            await discoveryService.FetchInstanceMetadataAsync(instance);
        }

        return await discoveryService.GetOrCreateActorWithDataAsync(
            actorUri,
            actorUri.Split('/').Last(),
            instance.Id
        );
    }

    private async Task<SnPost?> ResolvePostAsync(string objectUri)
    {
        if (!Uri.TryCreate(objectUri, UriKind.Absolute, out var uri))
            return await db.Posts.FirstOrDefaultAsync(c => c.FediverseUri == objectUri);

        var domain = uri.Host;
        if (domain != Domain)
            return await db.Posts.FirstOrDefaultAsync(c => c.FediverseUri == objectUri);

        var path = uri.AbsolutePath.Trim('/');
        var segments = path.Split('/');
        if (segments is not [.., "posts", var idStr])
            return null;

        if (Guid.TryParse(idStr, out var id))
            return await db.Posts.FirstOrDefaultAsync(p => p.Id == id);

        return null;
    }

    private async Task<SnPost?> FetchAndCreatePostAsync(string postUri, string actorUri)
    {
        try
        {
            var uri = new Uri(postUri);
            var domain = uri.Host;

            var instance = await db.FediverseInstances.FirstOrDefaultAsync(i => i.Domain == domain);
            if (instance == null)
            {
                instance = new SnFediverseInstance { Domain = domain, Name = domain };
                db.FediverseInstances.Add(instance);
                await db.SaveChangesAsync();
            }

            var actor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == actorUri);

            var fetched = await discoveryService.FetchActivityAsync(postUri, actorUri);
            if (fetched == null)
                return null;

            var objectDict = GetObject(fetched, "object") ?? fetched;
            var objectType = GetString(objectDict, "type");
            var objectId = GetString(objectDict, "id") ?? postUri;

            if (objectType != "Note" && objectType != "Article")
                return null;

            var existing = await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == objectId);
            if (existing != null)
                return existing;

            var post = new SnPost
            {
                FediverseUri = objectId,
                FediverseType = objectType == "Article"
                    ? DyFediverseContentType.DyFediverseArticle
                    : DyFediverseContentType.DyFediverseNote,
                Title = GetString(objectDict, "name"),
                Content = GetString(objectDict, "content"),
                ContentType = PostContentType.Html,
                PublishedAt = ParseInstant(GetValue(objectDict, "published")),
                ActorId = actor?.Id,
                Type = objectType == "Article" ? PostType.Article : PostType.Moment,
                Visibility = PostVisibility.Public
            };

            var inReplyTo = GetString(objectDict, "inReplyTo");
            if (!string.IsNullOrEmpty(inReplyTo))
            {
                var repliedTo = await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == inReplyTo);
                if (repliedTo != null)
                    post.RepliedPostId = repliedTo.Id;
            }

            db.Posts.Add(post);
            await db.SaveChangesAsync();

            logger.LogInformation("Fetched and created post: {Uri}", objectId);
            return post;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching post from {Uri}", postUri);
            return null;
        }
    }

    private static string ExtractDomain(string uri)
    {
        return new Uri(uri).Host;
    }

    private static string? GetObjectContent(Dictionary<string, object> activity)
    {
        var obj = GetObject(activity, "object");
        return obj != null ? GetString(obj, "content") : null;
    }

    private static string? GetString(Dictionary<string, object> dict, string key)
    {
        var value = dict.GetValueOrDefault(key);
        return value switch
        {
            string str => str,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.ToString(),
            _ => value?.ToString()
        };
    }

    private static object? GetValue(Dictionary<string, object> dict, string key)
    {
        return dict.GetValueOrDefault(key);
    }

    private static Dictionary<string, object>? GetObject(Dictionary<string, object> dict, string key)
    {
        var value = dict.GetValueOrDefault(key);
        if (value == null) return null;

        if (value is Dictionary<string, object> obj)
            return obj;

        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            var result = new Dictionary<string, object>();
            foreach (var prop in element.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Object => GetJsonObject(prop.Value),
                    JsonValueKind.Array => prop.Value.EnumerateArray().Select<JsonElement, object>(e => e.ValueKind switch
                    {
                        JsonValueKind.String => e.GetString() ?? "",
                        JsonValueKind.Object => GetJsonObject(e),
                        _ => e.ToString()
                    }).ToList(),
                    _ => prop.Value.ToString()
                };
            }
            return result;
        }

        return null;
    }

    private static Dictionary<string, object> GetJsonObject(JsonElement element)
    {
        var result = new Dictionary<string, object>();
        foreach (var prop in element.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => GetJsonObject(prop.Value),
                _ => prop.Value.ToString()
            };
        }
        return result;
    }

    private static Instant? ParseInstant(object? value)
    {
        if (value == null) return null;

        if (value is Instant instant) return instant;

        if (DateTimeOffset.TryParse(value.ToString(), out var dto))
            return Instant.FromDateTimeOffset(dto);

        return null;
    }

    private static (string symbol, PostReactionAttitude attitude) GetReactionFromEmoji(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji))
            return ("heart", PostReactionAttitude.Positive);

        return emoji switch
        {
            "👍" or "👍🏻" or "👍🏼" or "👍🏽" or "👍🏾" or "👍🏿" => ("thumb_up", PostReactionAttitude.Positive),
            "👎" or "👎🏻" or "👎🏼" or "👎🏽" or "👎🏾" or "👎🏿" => ("thumb_down", PostReactionAttitude.Negative),
            "❤️" or "♥️" => ("heart", PostReactionAttitude.Positive),
            "😂" or "🤣" => ("laugh", PostReactionAttitude.Positive),
            "🎉" => ("party", PostReactionAttitude.Positive),
            "🙏" or "🤗" => ("pray", PostReactionAttitude.Positive),
            "😕" => ("confuse", PostReactionAttitude.Neutral),
            "😡" => ("angry", PostReactionAttitude.Negative),
            "😐" => ("just_okay", PostReactionAttitude.Neutral),
            _ => ("heart", PostReactionAttitude.Positive)
        };
    }
}

public enum ActivityResult
{
    Success,
    Rejected,
    NotFound,
    BadRequest,
    NotSupported
}