using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NodaTime;
using System.Text.Json;
using DysonNetwork.Shared.Proto;
using ContentMention = DysonNetwork.Shared.Models.ContentMention;
using PostContentType = DysonNetwork.Shared.Models.PostContentType;
using PostPinMode = DysonNetwork.Shared.Models.PostPinMode;
using PostReactionAttitude = DysonNetwork.Shared.Models.PostReactionAttitude;
using PostType = DysonNetwork.Shared.Models.PostType;
using PostVisibility = DysonNetwork.Shared.Models.PostVisibility;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubActivityHandler(
    AppDatabase db,
    ActivityPubSignatureService signatureService,
    ActivityPubDeliveryService deliveryService,
    ActivityPubDiscoveryService discoveryService,
    FediverseModerationService moderationService,
    ILogger<ActivityPubActivityHandler> logger,
    IConfiguration configuration
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string BaseUrl => $"https://{Domain}";

    private async Task<SnPost?> GetPostByUriAsync(string objectUri)
    {
        if (!Uri.TryCreate(objectUri, UriKind.Absolute, out var uri))
            return await db.Posts.FirstOrDefaultAsync(c => c.FediverseUri == objectUri);

        var domain = uri.Host;

        // Remote post
        if (domain != Domain) return await db.Posts.FirstOrDefaultAsync(c => c.FediverseUri == objectUri);

        // Local post, extract ID from path like /posts/{guid}
        var path = uri.AbsolutePath.Trim('/');
        var segments = path.Split('/');
        if (segments is not [.., "posts", _]) return null;
        var idStr = segments[^1];
        if (Guid.TryParse(idStr, out var id))
        {
            return await db.Posts.FirstOrDefaultAsync(p => p.Id == id);
        }

        return null;
    }

    public async Task<bool> HandleIncomingActivityAsync(
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

        logger.LogDebug("Activity {ActivityType} details: {ActivityContent}",
            activityType, JsonSerializer.Serialize(activity));

        if (!signatureService.VerifyIncomingRequest(context, out var actorUri))
        {
            logger.LogInformation("Dropping activity due to failed signature verification. Type: {Type}, From: {Actor}",
                activityType, actor);
            return true;
        }

        if (string.IsNullOrEmpty(actorUri))
            return false;

        var actorDomain = ExtractDomainFromUri(actorUri);
        var content = ExtractContentFromActivity(activity);
        var moderationResult = await moderationService.CheckActorAsync(actorUri, content, actorDomain);

        if (moderationResult.IsBlocked)
        {
            logger.LogWarning("Blocked incoming {Type} from {ActorUri}. Matched rule: {Rule}",
                activityType, actorUri, moderationResult.MatchedRuleName);
            return false;
        }

        if (moderationResult.IsSilenced)
        {
            logger.LogInformation("Silenced incoming {Type} from {ActorUri}. Matched rule: {Rule}",
                activityType, actorUri, moderationResult.MatchedRuleName);
        }

        logger.LogInformation("Signature verified successfully. Handling {Type} from {ActorUri}",
            activityType, actorUri);

        try
        {
            switch (activityType)
            {
                case "Follow":
                    return await HandleFollowAsync(actorUri, activity);
                case "Accept":
                    return await HandleAcceptAsync(actorUri, activity);
                case "Reject":
                    return await HandleRejectAsync(actorUri, activity);
                case "QuoteRequest":
                    return await HandleQuoteRequestAsync(actorUri, activity);
                case "Undo":
                    return await HandleUndoAsync(actorUri, activity);
                case "Create":
                    return await HandleCreateAsync(actorUri, activity);
                case "Like":
                    return await HandleLikeAsync(actorUri, activity);
                case "EmojiReact":
                    return await HandleEmojiReactAsync(actorUri, activity);
                case "Announce":
                    return await HandleAnnounceAsync(actorUri, activity);
                case "Delete":
                    return await HandleDeleteAsync(activity);
                case "Update":
                    return await HandleUpdateAsync(actorUri, activity);
                case "Add":
                    return await HandleAddAsync(actorUri, activity);
                case "Remove":
                    return await HandleRemoveAsync(actorUri, activity);
                default:
                    logger.LogWarning("Unsupported activity type: {Type}. Full activity: {Activity}",
                        activityType, JsonSerializer.Serialize(activity));
                    return false;
            }
        }
        catch (InvalidOperationException err)
        {
            logger.LogError("Failed to handle activity: {Type}, due to {Message}.",
                activityType, err.Message);
            return false;
        }
    }

    private async Task<bool> HandleFollowAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        var activityId = activity.GetValueOrDefault("id")?.ToString();

        logger.LogInformation("Handling Follow. Actor: {ActorUri}, Target: {ObjectUri}, ActivityId: {Id}",
            actorUri, objectUri, activityId);

        if (string.IsNullOrEmpty(objectUri))
        {
            logger.LogWarning("Follow activity missing object field");
            return false;
        }

        var actor = await GetOrCreateActorAsync(actorUri);
        // This might be fail, but we assume it works great.
        var targetActor = await GetOrCreateActorAsync(objectUri);

        var existingRelationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r =>
                r.ActorId == actor.Id &&
                r.TargetActorId == targetActor.Id);

        switch (existingRelationship)
        {
            case { State: RelationshipState.Accepted }:
                logger.LogInformation(
                    "Follow relationship already exists and is accepted. ActorId: {ActorId}, TargetId: {TargetId}",
                    actor.Id, targetActor.Id);
                return true;
            case null:
                existingRelationship = new SnFediverseRelationship
                {
                    ActorId = actor.Id,
                    TargetActorId = targetActor.Id,
                    State = RelationshipState.Accepted,
                    FollowedBackAt = SystemClock.Instance.GetCurrentInstant()
                };
                db.FediverseRelationships.Add(existingRelationship);
                logger.LogInformation(
                    "Created new follow relationship. ActorId: {ActorId}, TargetActorId: {TargetId}",
                    actor.Id, targetActor.Id);
                break;
            default:
                existingRelationship.State = RelationshipState.Accepted;
                existingRelationship.FollowedBackAt = SystemClock.Instance.GetCurrentInstant();
                logger.LogInformation("Updating existing relationship. CurrentState: {State}, NewState: Accepted",
                    existingRelationship.State);
                break;
        }

        await db.SaveChangesAsync();

        await deliveryService.SendAcceptActivityAsync(
            targetActor,
            actorUri
        );

        logger.LogInformation("Handled follow from {Actor} to {Target}. RelationshipState: Accepted",
            actorUri, objectUri);
        return true;
    }

    private async Task<bool> HandleAcceptAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;

        var actor = await GetOrCreateActorAsync(actorUri);

        // Check if this is a QuoteAuthorization response
        var resultUri = GetStringValue(activity, "result");
        if (!string.IsNullOrEmpty(resultUri))
        {
            return await HandleQuoteAcceptAsync(actorUri, activity, objectUri, resultUri);
        }

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
                FollowedAt = SystemClock.Instance.GetCurrentInstant()
            };
            db.FediverseRelationships.Add(relationship);
        }
        else
        {
            relationship.State = RelationshipState.Accepted;
            relationship.FollowedAt = SystemClock.Instance.GetCurrentInstant();
        }

        await db.SaveChangesAsync();

        logger.LogInformation("Handled accept from {Actor}", actorUri);
        return true;
    }

    private async Task<bool> HandleRejectAsync(string actorUri, Dictionary<string, object> activity)
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
        relationship.RejectReason = "Remote rejected follow";

        await db.SaveChangesAsync();

        logger.LogInformation("Handled reject from {Actor}", actorUri);
        return true;
    }

    private async Task<bool> HandleQuoteRequestAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = GetStringValue(activity, "object");
        var instrumentUri = GetStringValue(activity, "instrument");
        var activityId = GetStringValue(activity, "id");
        
        logger.LogInformation("Handling QuoteRequest. Actor: {ActorUri}, Target: {ObjectUri}, Quote: {Instrument}",
            actorUri, objectUri, instrumentUri);

        if (string.IsNullOrEmpty(objectUri))
        {
            logger.LogWarning("QuoteRequest missing object field");
            return false;
        }

        var actor = await GetOrCreateActorAsync(actorUri);
        if (actor == null)
        {
            logger.LogWarning("QuoteRequest actor not found: {ActorUri}", actorUri);
            return false;
        }

        var targetPost = await db.Posts
            .FirstOrDefaultAsync(p => p.FediverseUri == objectUri);

        if (targetPost == null)
        {
            logger.LogWarning("QuoteRequest target post not found: {ObjectUri}", objectUri);
            return false;
        }

        var localActor = targetPost.PublisherId.HasValue
            ? await db.FediverseActors.FirstOrDefaultAsync(a => a.PublisherId == targetPost.PublisherId)
            : targetPost.Actor;

        if (localActor == null)
        {
            logger.LogWarning("Local author not found for quoted post");
            return false;
        }

        var quotePostUri = instrumentUri ?? GetStringValue(activity, "instrument")?.ToString();
        
        var shouldAutoApprove = true; 
        
        if (shouldAutoApprove)
        {
            var auth = new SnQuoteAuthorization
            {
                Id = Guid.NewGuid(),
                FediverseUri = $"{BaseUrl}/quote-authorizations/{Guid.NewGuid()}",
                AuthorId = localActor.Id,
                InteractingObjectUri = quotePostUri ?? activityId ?? "",
                InteractionTargetUri = objectUri,
                TargetPostId = targetPost.Id,
                IsValid = true
            };

            db.QuoteAuthorizations.Add(auth);
            await db.SaveChangesAsync();

            var acceptActivity = new Dictionary<string, object>
            {
                ["@context"] = "https://www.w3.org/ns/activitystreams",
                ["type"] = "Accept",
                ["id"] = $"{BaseUrl}/activities/{Guid.NewGuid()}",
                ["actor"] = localActor.Uri,
                ["to"] = actor.Uri,
                ["object"] = new Dictionary<string, object>
                {
                    ["type"] = "QuoteRequest",
                    ["id"] = activityId,
                    ["actor"] = actorUri,
                    ["object"] = objectUri,
                    ["instrument"] = quotePostUri
                },
                ["result"] = $"{BaseUrl}/quote-authorizations/{auth.Id}"
            };

            logger.LogInformation("Auto-approved quote request. Sending Accept to {ActorUri}", actorUri);
        }
        else
        {
            logger.LogInformation("Quote request requires manual approval for {ObjectUri}", objectUri);
        }

        return true;
    }

    private async Task<bool> HandleQuoteAcceptAsync(string actorUri, Dictionary<string, object> activity, string objectUri, string resultUri)
    {
        logger.LogInformation("Handling QuoteAuthorization Accept. Actor: {ActorUri}, Result: {ResultUri}",
            actorUri, resultUri);

        var quoteRequest = GetStringValue(activity, "instrument");
        if (string.IsNullOrEmpty(quoteRequest))
        {
            quoteRequest = GetStringValue(ConvertToDictionary(activity.GetValueOrDefault("instrument")), "id");
        }

        if (string.IsNullOrEmpty(quoteRequest))
        {
            logger.LogWarning("QuoteRequest instrument not found");
            return false;
        }

        var localActor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == objectUri);
        if (localActor == null)
        {
            logger.LogWarning("Local actor not found for accept: {ObjectUri}", objectUri);
            return false;
        }

        var targetPostUri = GetStringValue(activity, "object");
        
        var existingAuth = await db.QuoteAuthorizations
            .FirstOrDefaultAsync(q =>
                q.InteractionTargetUri == targetPostUri &&
                q.InteractingObjectUri == quoteRequest &&
                q.AuthorId == localActor.Id &&
                q.IsValid);

        if (existingAuth == null)
        {
            var targetPost = await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == targetPostUri);
            var quotePost = await db.Posts.FirstOrDefaultAsync(p => p.FediverseUri == quoteRequest);

            var auth = new SnQuoteAuthorization
            {
                Id = Guid.NewGuid(),
                FediverseUri = resultUri,
                AuthorId = localActor.Id,
                InteractingObjectUri = quoteRequest,
                InteractionTargetUri = targetPostUri,
                TargetPostId = targetPost?.Id,
                QuotePostId = quotePost?.Id,
                IsValid = true
            };

            db.QuoteAuthorizations.Add(auth);
            
            if (quotePost != null)
            {
                quotePost.QuoteAuthorizationId = auth.Id;
            }

            await db.SaveChangesAsync();
            logger.LogInformation("Created QuoteAuthorization: {AuthId}", auth.Id);
        }

        return true;
    }

    private async Task<bool> HandleUndoAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectValue = activity.GetValueOrDefault("object");
        var objectDict = ConvertToDictionary(objectValue);

        if (objectDict == null)
        {
            logger.LogWarning("Unable undo operation, no object found... {Object}", JsonSerializer.Serialize(activity));
            return false;
        }

        var objectType = GetStringValue(objectDict, "type");
        var objectUri = GetStringValue(objectDict, "object");

        return objectType switch
        {
            "Follow" => await UndoFollowAsync(actorUri, objectUri),
            "Like" => await UndoLikeAsync(actorUri, objectUri, null),
            "EmojiReact" => await UndoEmojiReactAsync(actorUri, objectDict),
            "Announce" => await UndoAnnounceAsync(actorUri, objectUri),
            _ => throw new InvalidOperationException($"Unhandled undo operation for {objectType}")
        };
    }

    private async Task<bool> HandleCreateAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectValue = activity.GetValueOrDefault("object");
        var objectDict = ConvertToDictionary(objectValue);

        if (objectDict == null)
            return false;

        var objectType = GetStringValue(objectDict, "type");
        if (objectType != "Note" && objectType != "Article")
        {
            logger.LogInformation("Skipping non-note content type: {Type}", objectType);
            return true;
        }

        var actor = await GetOrCreateActorAsync(actorUri);

        var contentUri = GetStringValue(objectDict, "id");
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
            FediverseType = objectType == "Article"
                ? DyFediverseContentType.DyFediverseArticle
                : DyFediverseContentType.DyFediverseNote,
            Title = GetStringValue(objectDict, "name"),
            Description = GetStringValue(objectDict, "summary"),
            Content = GetStringValue(objectDict, "content"),
            ContentType = objectDict.GetValueOrDefault("contentMap") != null
                ? PostContentType.Html
                : PostContentType.Markdown,
            PublishedAt = ParseInstant(objectDict.GetValueOrDefault("published")),
            EditedAt = ParseInstant(objectDict.GetValueOrDefault("updated")),
            ActorId = actor.Id,
            Language = GetStringValue(objectDict, "language"),
            Mentions = ParseMentions(objectDict.GetValueOrDefault("tag")),
            Attachments = ParseAttachments(objectDict.GetValueOrDefault("attachment")) ?? [],
            Type = objectType == "Article" ? PostType.Article : PostType.Moment,
            Visibility = PostVisibility.Public,
            Metadata = BuildMetadataFromActivity(objectDict)
        };
        
        var inReplyTo = GetStringValue(objectDict, "inReplyTo");
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            var replyingTo = await db.Posts
                .Where(c => c.FediverseUri == inReplyTo)
                .FirstOrDefaultAsync();
            if (replyingTo == null)
            {
                logger.LogWarning("Incoming post replied to {Uri}, but that post was not found in our database, skipping...", inReplyTo);
                return true;
            }

            content.RepliedPostId = replyingTo.Id;
        }
        
        var quoteUri = GetStringValue(objectDict, "quoteUri") 
            ?? GetStringValue(objectDict, "quoteUrl")
            ?? GetStringValue(objectDict, "_misskey_quote");
        
        if (!string.IsNullOrEmpty(quoteUri))
        {
            logger.LogInformation("Processing quote post with URI: {QuoteUri}", quoteUri);
            var forwardedItem = await db.Posts
                .FirstOrDefaultAsync(c => c.FediverseUri == quoteUri);
            if (forwardedItem == null)
            {
                logger.LogWarning("Incoming post quoted {Uri}, but the post not found in our database...", quoteUri);
                content.ForwardedGone = true;
            }
            else
            {
                content.ForwardedPostId = forwardedItem.Id;
            }
        }

        db.Posts.Add(content);
        await db.SaveChangesAsync();

        logger.LogInformation("Created federated content: {Uri}", contentUri);
        return true;
    }

    private async Task<bool> HandleLikeAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;

        var actor = await GetOrCreateActorAsync(actorUri);
        var content = await GetPostByUriAsync(objectUri);

        if (content == null)
        {
            logger.LogWarning("Content not found for like: {Uri}", objectUri);
            return false;
        }

        var contentStr = activity.GetValueOrDefault("content")?.ToString();
        var (symbol, attitude) = GetSymbolAndAttitudeFromEmoji(contentStr);

        var existingReaction = await db.PostReactions
            .FirstOrDefaultAsync(r =>
                r.ActorId == actor.Id &&
                r.PostId == content.Id &&
                r.Symbol == symbol);

        if (existingReaction != null)
        {
            logger.LogInformation("Like/EmojiReact already exists");
            return true;
        }

        var reaction = new SnPostReaction
        {
            FediverseUri = activity.GetValueOrDefault("id")?.ToString() ?? Guid.NewGuid().ToString(),
            Symbol = symbol,
            Attitude = attitude,
            IsLocal = false,
            PostId = content.Id,
            ActorId = actor.Id,
            Actor = actor,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UpdatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.PostReactions.Add(reaction);
        if (attitude == PostReactionAttitude.Positive)
            content.Upvotes++;
        else if (attitude == PostReactionAttitude.Negative)
            content.Downvotes++;

        await db.SaveChangesAsync();

        logger.LogInformation("Handled like/emojiReact from {Actor}", actorUri);
        return true;
    }

    private async Task<bool> HandleEmojiReactAsync(string actorUri, Dictionary<string, object> activity)
    {
        return await HandleLikeAsync(actorUri, activity);
    }

    private static (string symbol, PostReactionAttitude attitude) GetSymbolAndAttitudeFromEmoji(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji))
            return ("thumb_up", PostReactionAttitude.Positive);

        return emoji switch
        {
            "👍" => ("thumb_up", PostReactionAttitude.Positive),
            "👎" => ("thumb_down", PostReactionAttitude.Negative),
            "❤️" => ("heart", PostReactionAttitude.Positive),
            "😂" => ("laugh", PostReactionAttitude.Positive),
            "👏" => ("clap", PostReactionAttitude.Positive),
            "🎉" => ("party", PostReactionAttitude.Positive),
            "🙏" => ("pray", PostReactionAttitude.Positive),
            "😭" => ("cry", PostReactionAttitude.Negative),
            "😕" => ("confuse", PostReactionAttitude.Neutral),
            "😡" => ("angry", PostReactionAttitude.Negative),
            "😐" => ("just_okay", PostReactionAttitude.Neutral),
            _ => ("thumb_up", PostReactionAttitude.Positive)
        };
    }

    private async Task<bool> HandleAnnounceAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;

        var actor = await GetOrCreateActorAsync(actorUri);
        var content = await GetPostByUriAsync(objectUri);

        if (content == null)
        {
            logger.LogInformation("Boosted post not found locally, attempting to fetch: {Uri}", objectUri);
            content = await FetchAndCreatePostFromUriAsync(objectUri, actorUri);
            
            if (content == null)
            {
                logger.LogWarning("Could not fetch boosted post: {Uri}", objectUri);
                return false;
            }
        }

        var activityId = activity.GetValueOrDefault("id")?.ToString();
        var contentValue = GetStringValue(ConvertToDictionary(activity.GetValueOrDefault("object")), "content")
            ?? GetStringValue(activity, "content");
        
        var webUrl = GetStringValue(activity, "url") 
            ?? GetStringValue(ConvertToDictionary(activity.GetValueOrDefault("object")), "url");

        var existingBoost = await db.Boosts
            .FirstOrDefaultAsync(b => b.PostId == content.Id && b.ActorId == actor.Id);

        if (existingBoost != null)
        {
            logger.LogDebug("Boost already exists from {Actor} for post {PostId}", actorUri, content.Id);
            return true;
        }

        var boost = new SnBoost
        {
            PostId = content.Id,
            ActorId = actor.Id,
            ActivityPubUri = activityId,
            WebUrl = webUrl,
            Content = contentValue,
            BoostedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.Boosts.Add(boost);
        content.BoostCount++;
        await db.SaveChangesAsync();

        logger.LogInformation("Handled announce from {Actor} for post {PostId}", actorUri, content.Id);
        return true;
    }

    private async Task<SnPost?> FetchAndCreatePostFromUriAsync(string objectUri, string actorUri)
    {
        try
        {
            var uri = new Uri(objectUri);
            var domain = uri.Host;
            
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

            var actor = await db.FediverseActors
                .FirstOrDefaultAsync(a => a.Uri == actorUri);
            
            var postUri = objectUri;
            var webUrl = objectUri;
            
            if (uri.AbsolutePath.Contains("/objects/") || uri.AbsolutePath.Contains("/statuses/"))
            {
                webUrl = $"https://{domain}/@{uri.Segments.ElementAtOrDefault(1)?.Trim('/')}/{uri.Segments.LastOrDefault()}";
            }

            var activity = await discoveryService.FetchActivityAsync(postUri);
            if (activity == null)
            {
                logger.LogWarning("Failed to fetch activity from {Uri}", postUri);
                return null;
            }

            var objectValue = activity.GetValueOrDefault("object");
            var objectDict = ConvertToDictionary(objectValue);
            if (objectDict == null)
            {
                objectDict = activity;
            }

            var objectType = GetStringValue(objectDict, "type");
            if (objectType != "Note" && objectType != "Article")
            {
                logger.LogInformation("Skipping non-note content type in boost fetch: {Type}", objectType);
                return null;
            }

            var contentUri = GetStringValue(objectDict, "id");
            if (string.IsNullOrEmpty(contentUri))
                contentUri = postUri;

            var existingContent = await db.Posts
                .FirstOrDefaultAsync(c => c.FediverseUri == contentUri);

            if (existingContent != null)
                return existingContent;

            var content = new SnPost
            {
                FediverseUri = contentUri,
                FediverseType = objectType == "Article"
                    ? DyFediverseContentType.DyFediverseArticle
                    : DyFediverseContentType.DyFediverseNote,
                Title = GetStringValue(objectDict, "name"),
                Description = GetStringValue(objectDict, "summary"),
                Content = GetStringValue(objectDict, "content"),
                ContentType = objectDict.GetValueOrDefault("contentMap") != null
                    ? PostContentType.Html
                    : PostContentType.Markdown,
                PublishedAt = ParseInstant(objectDict.GetValueOrDefault("published")),
                EditedAt = ParseInstant(objectDict.GetValueOrDefault("updated")),
                ActorId = actor?.Id,
                Language = GetStringValue(objectDict, "language"),
                Mentions = ParseMentions(objectDict.GetValueOrDefault("tag")),
                Attachments = ParseAttachments(objectDict.GetValueOrDefault("attachment")) ?? [],
                Type = objectType == "Article" ? PostType.Article : PostType.Moment,
                Visibility = PostVisibility.Public,
                Metadata = BuildMetadataFromActivity(objectDict)
            };

            var inReplyTo = GetStringValue(objectDict, "inReplyTo");
            if (!string.IsNullOrEmpty(inReplyTo))
            {
                var replyingTo = await db.Posts
                    .FirstOrDefaultAsync(c => c.FediverseUri == inReplyTo);
                if (replyingTo != null)
                    content.RepliedPostId = replyingTo.Id;
            }

            var quoteUri = GetStringValue(objectDict, "quoteUri")
                ?? GetStringValue(objectDict, "quoteUrl")
                ?? GetStringValue(objectDict, "_misskey_quote");
            if (!string.IsNullOrEmpty(quoteUri))
            {
                var forwardedItem = await db.Posts
                    .FirstOrDefaultAsync(c => c.FediverseUri == quoteUri);
                if (forwardedItem != null)
                    content.ForwardedPostId = forwardedItem.Id;
                else
                    content.ForwardedGone = true;
            }

            db.Posts.Add(content);
            await db.SaveChangesAsync();

            logger.LogInformation("Fetched and created boosted post: {Uri}", contentUri);
            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching boosted post from {Uri}", objectUri);
            return null;
        }
    }

    private async Task<bool> HandleDeleteAsync(Dictionary<string, object> activity)
    {
        var objectUri = activity.GetValueOrDefault("object")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
            return false;

        var content = await GetPostByUriAsync(objectUri);
        if (content == null) return true;

        db.Remove(content);
        await db.SaveChangesAsync();

        logger.LogInformation("Handled federated Delete (tombstoned): {Uri}", objectUri);
        return true;
    }

    private async Task<bool> HandleUpdateAsync(string actorUri, Dictionary<string, object> activity)
    {
        var objectValue = activity.GetValueOrDefault("object");
        var objectDict = ConvertToDictionary(objectValue);

        if (objectDict == null)
            return false;

        var objectUri = GetStringValue(objectDict, "id");
        if (string.IsNullOrEmpty(objectUri))
            return false;

        var actor = await GetOrCreateActorAsync(actorUri);

        var content = await GetPostByUriAsync(objectUri);
        if (content == null)
        {
            content = new SnPost
            {
                FediverseUri = objectUri,
                FediverseType = DyFediverseContentType.DyFediverseNote,
                Type = PostType.Moment,
                Visibility = PostVisibility.Public,
                ActorId = actor.Id
            };
            db.Posts.Add(content);
        }

        content.Title = GetStringValue(objectDict, "name");
        content.Description = GetStringValue(objectDict, "summary");
        content.Content = GetStringValue(objectDict, "content");
        content.ContentType = PostContentType.Html;

        content.PublishedAt = ParseInstant(objectDict.GetValueOrDefault("published")) ?? content.PublishedAt;
        content.EditedAt = ParseInstant(objectDict.GetValueOrDefault("updated")) ??
                           SystemClock.Instance.GetCurrentInstant();

        content.Mentions = ParseMentions(objectDict.GetValueOrDefault("tag")) ??
                           new List<Shared.Models.ContentMention>();
        content.Attachments = ParseAttachments(objectDict.GetValueOrDefault("attachment")) ??
                              new List<Shared.Models.SnCloudFileReferenceObject>();
        content.Metadata = BuildMetadataFromActivity(objectDict);

        await db.SaveChangesAsync();

        logger.LogInformation("Handled federated Update: {Uri}", objectUri);
        return true;
    }

    private async Task<bool> UndoFollowAsync(string actorUri, string? targetActorUri)
    {
        if (string.IsNullOrEmpty(targetActorUri))
        {
            logger.LogInformation("Undid follow relationship failed, no target actor uri provided.");
            return false;
        }

        var actor = await GetOrCreateActorAsync(actorUri);
        var targetActor = await GetOrCreateActorAsync(targetActorUri);

        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => r.ActorId == actor.Id && r.TargetActorId == targetActor.Id);

        if (relationship == null) return true;
        db.Remove(relationship);
        await db.SaveChangesAsync();
        logger.LogInformation("Undid follow relationship");

        return true;
    }

    private async Task<bool> UndoLikeAsync(string actorUri, string? objectUri, Dictionary<string, object>? undoObjectDict = null)
    {
        if (string.IsNullOrEmpty(objectUri))
            return false;

        var actor = await GetOrCreateActorAsync(actorUri);
        var content = await GetPostByUriAsync(objectUri);

        if (content == null)
            return false;

        var contentStr = undoObjectDict != null ? GetStringValue(undoObjectDict, "content") : null;
        var (symbol, attitude) = GetSymbolAndAttitudeFromEmoji(contentStr);

        var reaction = await db.PostReactions
            .FirstOrDefaultAsync(r =>
                r.ActorId == actor.Id &&
                r.PostId == content.Id &&
                r.Symbol == symbol);

        if (reaction != null)
        {
            db.PostReactions.Remove(reaction);
            if (attitude == PostReactionAttitude.Positive)
                content.Upvotes--;
            else if (attitude == PostReactionAttitude.Negative)
                content.Downvotes--;
            await db.SaveChangesAsync();
        }

        return true;
    }

    private async Task<bool> UndoEmojiReactAsync(string actorUri, Dictionary<string, object> undoObjectDict)
    {
        var objectUri = GetStringValue(undoObjectDict, "object");
        var content = string.IsNullOrEmpty(objectUri) ? null : await GetPostByUriAsync(objectUri);

        if (content == null)
            return false;

        var emoji = GetStringValue(undoObjectDict, "content");
        var (symbol, attitude) = GetSymbolAndAttitudeFromEmoji(emoji);

        var actor = await GetOrCreateActorAsync(actorUri);
        var reaction = await db.PostReactions
            .FirstOrDefaultAsync(r =>
                r.ActorId == actor.Id &&
                r.PostId == content.Id &&
                r.Symbol == symbol);

        if (reaction != null)
        {
            db.PostReactions.Remove(reaction);
            if (attitude == PostReactionAttitude.Positive)
                content.Upvotes--;
            else if (attitude == PostReactionAttitude.Negative)
                content.Downvotes--;
            await db.SaveChangesAsync();
        }

        return true;
    }

    private async Task<bool> UndoAnnounceAsync(string actorUri, string? objectUri)
    {
        if (string.IsNullOrEmpty(objectUri))
            return false;

        var actor = await GetOrCreateActorAsync(actorUri);
        var content = await GetPostByUriAsync(objectUri);

        if (content == null)
        {
            logger.LogWarning("Cannot undo announce - post not found: {Uri}", objectUri);
            return false;
        }

        var boost = await db.Boosts
            .FirstOrDefaultAsync(b => b.PostId == content.Id && b.ActorId == actor.Id);

        if (boost != null)
        {
            db.Boosts.Remove(boost);
            content.BoostCount = Math.Max(0, content.BoostCount - 1);
            await db.SaveChangesAsync();
            logger.LogInformation("Removed boost from {Actor} for post {PostId}", actorUri, content.Id);
        }
        else
        {
            logger.LogWarning("Boost record not found when undoing - actor: {Actor}, post: {Uri}", actorUri, objectUri);
        }

        return true;
    }

    private async Task<bool> HandleAddAsync(string actorUri, Dictionary<string, object> activity)
    {
        var target = ConvertToDictionary(activity.GetValueOrDefault("target"));
        if (target == null)
        {
            logger.LogWarning("Add activity missing target");
            return false;
        }

        var targetType = target.GetValueOrDefault("type")?.ToString();
        if (targetType != "Collection" && targetType != "OrderedCollection")
        {
            logger.LogDebug("Add activity target is not a collection, skipping: {Type}", targetType);
            return false;
        }

        var collection = target.GetValueOrDefault("id")?.ToString();
        if (collection == null || !collection.Contains("/featured"))
        {
            logger.LogDebug("Add activity target is not featured collection: {Collection}", collection);
            return false;
        }

        var obj = ConvertToDictionary(activity.GetValueOrDefault("object"));
        if (obj == null)
        {
            logger.LogWarning("Add activity missing object");
            return false;
        }

        var objectUri = obj.GetValueOrDefault("id")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
        {
            logger.LogWarning("Add activity object missing id");
            return false;
        }

        var post = await GetPostByUriAsync(objectUri);
        if (post == null)
        {
            logger.LogWarning("Add activity: post not found: {Uri}", objectUri);
            return false;
        }

        var actor = await GetOrCreateActorAsync(actorUri);
        var actorInstanceDomain = actor.Instance?.Domain;
        
        var localPublisher = await db.Publishers
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Members.Any(m => m.AccountId != null));

        if (localPublisher == null)
        {
            logger.LogWarning("Add activity: local publisher not found");
            return false;
        }

        if (post.PublisherId != localPublisher.Id)
        {
            logger.LogWarning("Add activity: post does not belong to local publisher");
            return false;
        }

        if (post.PinMode != PostPinMode.PublisherPage)
        {
            post.PinMode = PostPinMode.PublisherPage;
            await db.SaveChangesAsync();
            logger.LogInformation("Pinned post {PostId} via Add activity", post.Id);
        }

        return true;
    }

    private async Task<bool> HandleRemoveAsync(string actorUri, Dictionary<string, object> activity)
    {
        var target = ConvertToDictionary(activity.GetValueOrDefault("target"));
        if (target == null)
        {
            logger.LogWarning("Remove activity missing target");
            return false;
        }

        var targetType = target.GetValueOrDefault("type")?.ToString();
        if (targetType != "Collection" && targetType != "OrderedCollection")
        {
            logger.LogDebug("Remove activity target is not a collection, skipping: {Type}", targetType);
            return false;
        }

        var collection = target.GetValueOrDefault("id")?.ToString();
        if (collection == null || !collection.Contains("/featured"))
        {
            logger.LogDebug("Remove activity target is not featured collection: {Collection}", collection);
            return false;
        }

        var obj = ConvertToDictionary(activity.GetValueOrDefault("object"));
        if (obj == null)
        {
            logger.LogWarning("Remove activity missing object");
            return false;
        }

        var objectUri = obj.GetValueOrDefault("id")?.ToString();
        if (string.IsNullOrEmpty(objectUri))
        {
            logger.LogWarning("Remove activity object missing id");
            return false;
        }

        var post = await GetPostByUriAsync(objectUri);
        if (post == null)
        {
            logger.LogWarning("Remove activity: post not found: {Uri}", objectUri);
            return false;
        }

        if (post.PinMode == PostPinMode.PublisherPage)
        {
            post.PinMode = (PostPinMode)0;
            await db.SaveChangesAsync();
            logger.LogInformation("Unpinned post {PostId} via Remove activity", post.Id);
        }

        return true;
    }

    private async Task<SnFediverseActor> GetOrCreateActorAsync(string actorUri)
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor != null) return actor;

        var instance = await GetOrCreateInstanceAsync(actorUri);
        actor = new SnFediverseActor
        {
            Uri = actorUri,
            Username = ExtractUsernameFromUri(actorUri),
            DisplayName = ExtractUsernameFromUri(actorUri),
            InstanceId = instance.Id
        };

        try
        {
            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();
            await discoveryService.FetchActorDataAsync(actor);
            return actor;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            // Race condition - another request created the actor, fetch it instead
            logger.LogInformation("Actor was created by another request, fetching: {ActorUri}", actorUri);
            var existing = await db.FediverseActors
                .FirstOrDefaultAsync(a => a.Uri == actorUri);
            
            if (existing != null && string.IsNullOrEmpty(existing.PublicKey))
            {
                await discoveryService.FetchActorDataAsync(existing);
            }
            return existing ?? actor;
        }
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

    private string? ExtractContentFromActivity(Dictionary<string, object> activity)
    {
        var objectValue = activity.GetValueOrDefault("object");
        if (objectValue == null)
            return null;

        if (objectValue is JsonElement element && element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("content", out var contentElement))
            {
                return contentElement.ValueKind == JsonValueKind.String ? contentElement.GetString() : null;
            }
        }
        else if (objectValue is Dictionary<string, object> dict)
        {
            return GetStringValue(dict, "content");
        }

        return null;
    }

    private static string? GetStringValue(Dictionary<string, object> dict, string key)
    {
        var value = dict.GetValueOrDefault(key);
        if (value == null)
            return null;

        return value switch
        {
            string str => str,
            JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString(),
            _ => value.ToString()
        };
    }

    private static int? TryGetIntValue(Dictionary<string, object> dict, string key)
    {
        var value = dict.GetValueOrDefault(key);
        if (value == null)
            return null;

        return value switch
        {
            int i => i,
            long l => (int)l,
            JsonElement { ValueKind: JsonValueKind.Number } element => element.TryGetInt32(out var result)
                ? result
                : (int?)element.GetDouble(),
            _ => null
        };
    }

    private static Dictionary<string, object>? ConvertToDictionary(object? value)
    {
        if (value == null)
            return null;

        if (value is Dictionary<string, object> dict)
            return dict;

        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            var result = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = ConvertJsonElementToObject(property.Value);
            }

            return result;
        }

        return null;
    }

    private static object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            JsonValueKind.Object => ConvertToDictionary(element) ?? new Dictionary<string, object>(),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToObject).ToList(),
            _ => element.ToString()
        };
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
        if (value == null)
            return null;

        var attachments = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } element
                => element.EnumerateArray().Select(ConvertJsonElementToObject).ToList(),
            List<object> list => list,
            _ => null
        };

        return attachments?.OfType<Dictionary<string, object>>()
            .Select(dict => new SnCloudFileReferenceObject
            {
                Id = Guid.NewGuid().ToString(),
                Name = GetStringValue(dict, "name") ?? string.Empty,
                Url = GetStringValue(dict, "url"),
                MimeType = GetStringValue(dict, "mediaType"),
                Width = TryGetIntValue(dict, "width"),
                Height = TryGetIntValue(dict, "height"),
                Blurhash = GetStringValue(dict, "blurhash"),
                FileMeta = new Dictionary<string, object?>(),
                UserMeta = new Dictionary<string, object?>(),
                Size = 0,
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                UpdatedAt = SystemClock.Instance.GetCurrentInstant()
            })
            .ToList();
    }

    private static List<ContentMention>? ParseMentions(object? value)
    {
        if (value == null)
            return null;

        var tags = value switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } element
                => element.EnumerateArray().Select(ConvertJsonElementToObject).ToList(),
            List<object> list => list,
            _ => null
        };

        return tags?.Where(tag => tag is Dictionary<string, object> dict && GetStringValue(dict, "type") == "Mention")
            .Select(tag => (Dictionary<string, object>)tag)
            .Select(dict => new ContentMention
            {
                Username = GetStringValue(dict, "name"),
                ActorUri = GetStringValue(dict, "href")
            })
            .ToList();
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
                metadata["fediverse_tags"] = fediverseTags;
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
                metadata["fediverse_emojis"] = emojis;
            }
        }

        var fediverseUrl = objectDict.GetValueOrDefault("url");
        if (fediverseUrl is not null)
            metadata["fediverse_url"] = fediverseUrl.ToString()!;

        return metadata;
    }
}