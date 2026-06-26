using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Models;
using System.Globalization;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Wallet;
using DysonNetwork.Sphere.Live;

using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NodaTime;
using Swashbuckle.AspNetCore.Annotations;
using PostType = DysonNetwork.Shared.Models.PostType;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;
using PublisherService = DysonNetwork.Sphere.Publisher.PublisherService;
using PollsService = DysonNetwork.Sphere.Poll.PollService;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
public class PostActionController(
    AppDatabase db,
    PostService ps,
    PublisherService pub,
    PostCollectionService pcs,
    DyProfileService.DyProfileServiceClient accounts,
    RemoteActionLogService als,
    RemotePaymentService remotePayments,
    PollsService polls,
    RemoteRealmService rs,
    LiveStreamService liveStreams,
    ActivityPubDeliveryService activityPubDelivery,
    SponsorService sponsorService,
    ILogger<PostActionController> logger,
    IEventBus eventBus
) : ControllerBase
{
    private static bool HasLocationPayload(string? locationName, string? locationAddress, string? locationWkt)
    {
        return !string.IsNullOrWhiteSpace(locationName)
            || !string.IsNullOrWhiteSpace(locationAddress)
            || !string.IsNullOrWhiteSpace(locationWkt);
    }

    private static LocationEmbed CreateLocationEmbed(
        string? locationName,
        string? locationAddress,
        Geometry? location
    )
    {
        return new LocationEmbed
        {
            Name = string.IsNullOrWhiteSpace(locationName) ? null : locationName,
            Address = string.IsNullOrWhiteSpace(locationAddress) ? null : locationAddress,
            Wkt = location?.AsText()
        };
    }

    private ActionResult? TryParseLocation(string? locationWkt, out Geometry? location)
    {
        location = null;
        if (string.IsNullOrWhiteSpace(locationWkt))
            return null;

        try
        {
            location = new WKTReader().Read(locationWkt);
            location.SRID = 4326;
            return null;
        }
        catch (Exception)
        {
            return BadRequest("Invalid location WKT.");
        }
    }

    private async Task<bool> IsBlockedEitherDirectionAsync(Guid userId, Guid otherId)
    {
        try
        {
            var response = await accounts.HasRelationshipAsync(new DyGetRelationshipRequest
            {
                AccountId = userId.ToString(),
                RelatedId = otherId.ToString(),
                Status = (int)RelationshipStatus.Blocked,
                EitherDirection = true
            });
            return response.Value;
        }
        catch
        {
            logger.LogError("Unable to get relationship status, skipping...");
            return false;
        }
    }

    public class PostRequest
    {
        [MaxLength(1024)] public string? Title { get; set; }

        [MaxLength(4096)] public string? Description { get; set; }

        [MaxLength(1024)] public string? Slug { get; set; }
        public string? Content { get; set; }

        public Shared.Models.PostVisibility? Visibility { get; set; } =
            Shared.Models.PostVisibility.Public;

        public PostType? Type { get; set; }
        public Shared.Models.PostEmbedView? EmbedView { get; set; }

        [MaxLength(16)] public List<string>? Tags { get; set; }
        [MaxLength(8)] public List<string>? Categories { get; set; }
        [MaxLength(32)] public List<string>? Attachments { get; set; }

        public Dictionary<string, object>? Meta { get; set; }
        public Instant? DraftedAt { get; set; }
        public Instant? PublishedAt { get; set; }
        public Guid? RepliedPostId { get; set; }
        public Guid? ForwardedPostId { get; set; }
        public Guid? RealmId { get; set; }

        public Guid? PollId { get; set; }
        public Guid? FundId { get; set; }
        public Guid? MeetId { get; set; }
        public Guid? LiveStreamId { get; set; }
        public Guid? NotableDayId { get; set; }
        public Guid? CalendarEventId { get; set; }
        

        [MaxLength(256)] public string? LocationName { get; set; }

        [MaxLength(1024)] public string? LocationAddress { get; set; }

        public string? LocationWkt { get; set; }
        
        public string? ThumbnailId { get; set; }

        // Client-controlled embeds list — when provided, bypasses per-field logic
        public List<Dictionary<string, object>>? Embeds { get; set; }

        [MaxLength(16)]
        public string? Language { get; set; }

        [MaxLength(16)] public List<Guid>? CollectionIds { get; set; }
    }

    public class BatchDeleteRequest
    {
        public List<Guid> PostIds { get; set; } = [];
    }

    public class BatchVisibilityRequest
    {
        public List<Guid> PostIds { get; set; } = [];
        public Shared.Models.PostVisibility? Visibility { get; set; }
        public Instant? DraftedAt { get; set; }
        public Instant? PublishedAt { get; set; }
    }

    [HttpPost]
    [AskPermission("posts.create")]
    public async Task<ActionResult<SnPost>> CreatePost(
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (request.Type != PostType.Blog)
        {
            if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
                return BadRequest("Content is required.");
        }
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        // Blog posts require the posts.create.blog permission
        if (request.Type == PostType.Blog)
        {
            if (!currentUser.IsSuperuser)
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var permissionService = scope.ServiceProvider.GetRequiredService<DyPermissionService.DyPermissionServiceClient>();
                var permResp = await permissionService.HasPermissionAsync(new DyHasPermissionRequest
                {
                    Actor = currentUser.Id,
                    Key = "posts.create.blog"
                });
                if (!permResp.HasPermission)
                    return StatusCode(403, "You do not have permission to create blog posts.");
            }

            if (string.IsNullOrWhiteSpace(request.Content) || !Uri.TryCreate(request.Content, UriKind.Absolute, out _))
                return BadRequest("Blog post content must be a valid URL.");
        }

        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) && request.Type != PostType.Article)
            return BadRequest("Thumbnail only supported in article.");
        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) &&
            !(request.Attachments?.Contains(request.ThumbnailId) ?? false))
            return BadRequest("Thumbnail must be presented in attachment list.");
        if (request.DraftedAt is not null && request.PublishedAt is not null)
            return BadRequest("Cannot set both draftedAt and publishedAt.");

        var locationError = TryParseLocation(request.LocationWkt, out var location);
        if (locationError is not null)
            return locationError;

        var accountId = Guid.Parse(currentUser.Id);

        SnPublisher? publisher;
        if (pubName is null)
        {
            var settings = await db.PublishingSettings
                .FirstOrDefaultAsync(s => s.AccountId == accountId);
            var isReply = request.RepliedPostId != null;
            var defaultPublisherId = isReply
                ? settings?.DefaultReplyPublisherId
                : settings?.DefaultPostingPublisherId;

            if (defaultPublisherId != null)
            {
                publisher = await db.Publishers
                    .FirstOrDefaultAsync(p => p.Id == defaultPublisherId);
                if (publisher != null && await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                {
                    // Use default publisher
                }
                else
                {
                    publisher = null;
                }
            }
            else
            {
                publisher = null;
            }

            if (publisher == null)
            {
                publisher = await db.Publishers.FirstOrDefaultAsync(e =>
                    e.AccountId == accountId && e.Type == Shared.Models.PublisherType.Individual
                );
            }
        }
        else
        {
            publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null)
                return BadRequest("Publisher was not found.");
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return StatusCode(403, "You need at least be an editor to post as this publisher.");
        }

        if (publisher is null)
            return BadRequest("Publisher was not found.");

        // Domain verification for blog posts
        if (request.Type == PostType.Blog && !currentUser.IsSuperuser)
        {
            if (!Uri.TryCreate(request.Content, UriKind.Absolute, out var blogUri))
                return BadRequest("Blog post content must be a valid URL.");

            var host = blogUri.Host.ToLowerInvariant();
            var isDomainVerified = await db.PublisherVerifiedDomains
                .AnyAsync(d => d.PublisherId == publisher.Id
                    && d.Domain == host
                    && d.Status == DomainVerificationStatus.Verified);
            if (!isDomainVerified)
                return StatusCode(403, "This domain is not verified for your publisher. Add it via the domains endpoint first.");
        }

        var post = new SnPost
        {
            Title = request.Title,
            Description = request.Description,
            Slug = request.Slug,
            Content = request.Content,
            Visibility = request.Visibility ?? Shared.Models.PostVisibility.Public,
            DraftedAt = request.DraftedAt,
            PublishedAt = request.PublishedAt,
            Type = request.Type ?? PostType.Moment,
            Metadata = request.Meta,
            EmbedView = request.Type == PostType.Blog
                ? new PostEmbedView { Uri = request.Content!, Renderer = PostEmbedViewRenderer.WebView }
                : request.EmbedView,
            Language = request.Language,
            Publisher = publisher,
        };

        if (post.Visibility is Shared.Models.PostVisibility.CloseFriendsOnly or Shared.Models.PostVisibility.Friends)
        {
            if (publisher is null)
                return BadRequest("CloseFriendsOnly and Friends visibility require a publisher.");
        }

        if (request.RepliedPostId is not null)
        {
            var repliedPost = await db
                .Posts.Where(p => p.Id == request.RepliedPostId.Value)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();
            if (repliedPost is null)
                return BadRequest("Post replying to was not found.");

            if (repliedPost.Publisher?.AccountId != null)
            {
                if (await IsBlockedEitherDirectionAsync(repliedPost.Publisher.AccountId.Value, accountId))
                    return BadRequest("You cannot reply to a post from a user who blocked you.");
            }
            
            post.RepliedPost = repliedPost;
            post.RepliedPostId = repliedPost.Id;
        }

        if (request.ForwardedPostId is not null)
        {
            var forwardedPost = await db
                .Posts.Where(p => p.Id == request.ForwardedPostId.Value)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();
            if (forwardedPost is null)
                return BadRequest("Forwarded post was not found.");
            if (forwardedPost.Publisher?.AccountId != null && forwardedPost.Publisher.AccountId != accountId)
            {
                if (await IsBlockedEitherDirectionAsync(forwardedPost.Publisher.AccountId.Value, accountId))
                    return BadRequest("You cannot forward a post from a blocked user.");
            }
            post.ForwardedPost = forwardedPost;
            post.ForwardedPostId = forwardedPost.Id;
        }

        if (request.RealmId is not null)
        {
            var realm = await rs.GetRealm(request.RealmId.Value.ToString());
            if (
                !await rs.IsMemberWithRole(
                    realm.Id,
                    accountId,
                    new List<int> { RealmMemberRole.Normal }
                )
            )
                return StatusCode(403, "You are not a member of this realm.");
            post.RealmId = realm.Id;
        }

        // All the fields are updated when the request contains the specific fields
        // But the Poll can be null, so it will be updated whatever it included in requests or not
        // If client provides the complete embeds list, use it directly
        if (request.Embeds is { Count: > 0 })
        {
            post.Metadata ??= new Dictionary<string, object>();
            post.Metadata["embeds"] = request.Embeds;
        }
        else if (request.PollId.HasValue)
        {
            try
            {
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                // Remove all old poll embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && type.ToString() == "poll"
                );
                embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            // Remove all old poll embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "poll");
        }

        // Handle fund embeds
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await remotePayments.GetWalletFund(request.FundId.Value.ToString());

                // Check if the fund was created by the current user
                if (fundResponse.CreatorAccountId != currentUser.Id)
                    return BadRequest("You can only share funds that you created.");

                var fundEmbed = new FundEmbed { Id = request.FundId.Value };
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                embeds.Add(EmbeddableBase.ToDictionary(fundEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("The specified fund does not exist.");
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest("Invalid fund ID.");
            }
        }

        if (request.MeetId.HasValue)
        {
            var meetEmbed = new MeetEmbed { Id = request.MeetId.Value };
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.Add(EmbeddableBase.ToDictionary(meetEmbed));
            post.Metadata["embeds"] = embeds;
        }

        if (request.LiveStreamId.HasValue)
        {
            try
            {
                var liveStream = await liveStreams.GetByIdAsync(request.LiveStreamId.Value);
                if (liveStream == null)
                    return BadRequest("The specified live stream does not exist.");

                // Check if the live stream belongs to the current user's publisher
                if (liveStream.PublisherId != publisher.Id)
                    return BadRequest("You can only share live streams from your own publisher.");

                var liveStreamEmbed = new LiveStreamEmbed { Id = request.LiveStreamId.Value };
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                embeds.Add(EmbeddableBase.ToDictionary(liveStreamEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest($"Error attaching live stream: {ex.Message}");
            }
        }

        if (HasLocationPayload(request.LocationName, request.LocationAddress, request.LocationWkt))
        {
            var locationEmbed = CreateLocationEmbed(request.LocationName, request.LocationAddress, location);
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.Add(EmbeddableBase.ToDictionary(locationEmbed));
            post.Metadata["embeds"] = embeds;
        }

        if (request.NotableDayId.HasValue)
        {
            var notableDayEmbed = new NotableDayEmbed { Id = request.NotableDayId.Value };
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.Add(EmbeddableBase.ToDictionary(notableDayEmbed));
            post.Metadata["embeds"] = embeds;
        }

        if (request.CalendarEventId.HasValue)
        {
            var calendarEventEmbed = new CalendarEventEmbed { Id = request.CalendarEventId.Value };
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.Add(EmbeddableBase.ToDictionary(calendarEventEmbed));
            post.Metadata["embeds"] = embeds;
        }

        if (request.ThumbnailId is not null)
        {
            post.Metadata ??= new Dictionary<string, object>();
            post.Metadata["thumbnail"] = request.ThumbnailId;
        }

        try
        {
            post = await ps.PostAsync(
                post,
                attachments: request.Attachments,
                tags: request.Tags,
                categories: request.Categories,
                actor: currentUser
            );
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        if (request.CollectionIds is { Count: > 0 })
        {
            var collections = await db.PostCollections
                .Where(c => request.CollectionIds.Contains(c.Id) && c.PublisherId == publisher.Id)
                .ToListAsync();

            foreach (var collection in collections)
            {
                try
                {
                    await pcs.AddPostAsync(collection, post.Id, null);
                }
                catch (InvalidOperationException)
                {
                    // Skip duplicates silently
                }
            }
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostCreate,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        if (post.Tags.Count > 0 && post.Categories.Count > 0)
        {
            als.CreateActionLog(
                Guid.Parse(currentUser.Id),
                ActionLogType.PostCreateTopical,
                new Dictionary<string, object>
                {
                    { "post_id", post.Id.ToString() },
                    { "tag_count", post.Tags.Count },
                    { "category_count", post.Categories.Count }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );
        }

        post.Publisher = publisher;

        if (post.RealmId.HasValue && post.DraftedAt is null && post.PublishedAt is not null &&
            post.PublishedAt.Value <= SystemClock.Instance.GetCurrentInstant())
        {
            await eventBus.PublishAsync(new RealmActivityEvent
            {
                RealmId = post.RealmId.Value,
                AccountId = Guid.Parse(currentUser.Id),
                ActivityType = "post_created",
                ReferenceId = post.Id.ToString(),
                Delta = 20
            });
        }

        return post;
    }

    public class PostReactionRequest
    {
        [MaxLength(256)] public string Symbol { get; set; } = null!;
        public Shared.Models.PostReactionAttitude Attitude { get; set; }
    }

    public static readonly List<string> ReactionsAllowedDefault =
    [
        "thumb_up",
        "thumb_down",
        "just_okay",
        "cry",
        "confuse",
        "clap",
        "laugh",
        "angry",
        "party",
        "pray",
        "heart",
    ];

    [HttpPost("{id:guid}/reactions")]
    [Authorize]
    [AskPermission("posts.react")]
    public async Task<ActionResult<SnPostReaction>> ReactPost(
        Guid id,
        [FromBody] PostReactionRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        if (!ReactionsAllowedDefault.Contains(request.Symbol))
            if (currentUser.PerkSubscription is null)
                return BadRequest("You need subscription to send custom reactions");

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);

        if (post.Publisher?.AccountId != null && post.Publisher.AccountId != accountId)
        {
            if (await IsBlockedEitherDirectionAsync(post.Publisher.AccountId.Value, accountId))
                return BadRequest("You cannot react to this post.");
        }
        
        var isSelfReact =
            post.Publisher?.AccountId is not null && post.Publisher.AccountId == accountId;

        var isExistingReaction = await db.PostReactions.AnyAsync(r =>
            r.PostId == post.Id && r.Symbol == request.Symbol && r.AccountId == accountId
        );
        var reaction = new SnPostReaction
        {
            Symbol = request.Symbol,
            Attitude = request.Attitude,
            PostId = post.Id,
            AccountId = accountId,
        };
        var isRemoving = await ps.ModifyPostVotes(
            post,
            reaction,
            currentUser,
            isExistingReaction,
            isSelfReact
        );

        if (isRemoving)
            return NoContent();

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostReact,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() },
                { "reaction", request.Symbol },
                { "post_kind", post.PublisherId.HasValue ? "publisher" : "personal" }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(reaction);
    }

    [HttpPost("{id:guid}/bookmark")]
    [Authorize]
    public async Task<ActionResult<SnPostBookmark>> BookmarkPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var accountId = Guid.Parse(currentUser.Id);
        var userPublishers = await pub.GetUserPublishers(accountId);

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var existingBookmark = await db.PostBookmarks
            .Where(b => b.PostId == id && b.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (existingBookmark is not null)
            return Ok(existingBookmark);

        var bookmark = new SnPostBookmark
        {
            PostId = id,
            AccountId = accountId,
        };

        db.PostBookmarks.Add(bookmark);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            accountId,
            ActionLogType.PostBookmark,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(bookmark);
    }

    [HttpDelete("{id:guid}/bookmark")]
    [Authorize]
    public async Task<ActionResult> UnbookmarkPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var bookmark = await db.PostBookmarks
            .Where(b => b.PostId == id && b.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (bookmark is null)
            return NotFound();

        db.PostBookmarks.Remove(bookmark);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            accountId,
            ActionLogType.PostUnbookmark,
            new Dictionary<string, object>
            {
                { "post_id", id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return NoContent();
    }

    [HttpGet("{id:guid}/bookmark")]
    [Authorize]
    public async Task<ActionResult<SnPostBookmark?>> GetBookmarkStatus(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var bookmark = await db.PostBookmarks
            .Where(b => b.PostId == id && b.AccountId == accountId)
            .FirstOrDefaultAsync();

        return Ok(bookmark);
    }

    public class PostAwardRequest
    {
        public decimal Amount { get; set; }
        public Shared.Models.PostReactionAttitude Attitude { get; set; }

        [MaxLength(4096)] public string? Message { get; set; }
    }

    [HttpGet("{id:guid}/awards")]
    public async Task<ActionResult<SnPostAward>> GetPostAwards(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var queryable = db.PostAwards.Where(a => a.PostId == id).AsQueryable();

        var totalCount = await queryable.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var awards = await queryable.Take(take).Skip(offset).ToListAsync();

        return Ok(awards);
    }

    [HttpGet("{id:guid}/awards/pending")]
    public async Task<ActionResult> GetPendingPostAwards(Guid id)
    {
        var pendingAwards = await db.PostAwards
            .Where(a => a.PostId == id && a.SettledAt == null && a.Attitude == PostReactionAttitude.Positive)
            .ToListAsync();

        var totalAmount = pendingAwards.Sum(a => a.Amount);
        var payoutAmount = totalAmount * 0.80m;

        return Ok(new
        {
            count = pendingAwards.Count,
            total_amount = totalAmount,
            payout_amount = payoutAmount,
        });
    }

    public class PostAwardResponse
    {
        public Guid OrderId { get; set; }
    }

    [HttpPost("{id:guid}/awards")]
    [Authorize]
    public async Task<ActionResult<PostAwardResponse>> AwardPost(
        Guid id,
        [FromBody] PostAwardRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        if (request.Attitude == PostReactionAttitude.Neutral)
            return BadRequest("You cannot create a neutral post award");

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { AccountId = currentUser.Id }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);

        if (post.Publisher?.AccountId != null && post.Publisher.AccountId != accountId)
        {
            if (await IsBlockedEitherDirectionAsync(post.Publisher.AccountId.Value, accountId))
                return BadRequest("You cannot award this post.");
        }

        var orderRemark = string.IsNullOrWhiteSpace(post.Title)
            ? "from @" + (post.Publisher?.Name ?? "unknown")
            : post.Title;
        var order = await remotePayments.CreateOrder(
            currency: "points",
            amount: request.Amount.ToString(CultureInfo.InvariantCulture),
            productIdentifier: "posts.award",
            remarks: $"Award post {orderRemark}",
            meta: InfraObjectCoder.ConvertObjectToByteString(
                new Dictionary<string, object?>
                {
                    ["account_id"] = accountId,
                    ["post_id"] = post.Id,
                    ["amount"] = request.Amount.ToString(CultureInfo.InvariantCulture),
                    ["message"] = request.Message,
                    ["attitude"] = request.Attitude,
                }
            ).ToByteArray()
        );

        return Ok(new PostAwardResponse() { OrderId = Guid.Parse(order.Id) });
    }

    public class PostSponsorRequest
    {
        public decimal Amount { get; set; }
    }

    public class PostSponsorResponse
    {
        public Guid OrderId { get; set; }
        public decimal Amount { get; set; }
    }

    [HttpPost("{id:guid}/sponsor")]
    [Authorize]
    public async Task<ActionResult<PostSponsorResponse>> SponsorPost(
        Guid id,
        [FromBody] PostSponsorRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        if (request.Amount < SponsorService.MinimumBidAmount)
            return BadRequest($"Minimum sponsorship bid is {SponsorService.MinimumBidAmount} golds.");

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();
        if (post.Visibility != Shared.Models.PostVisibility.Public)
            return BadRequest("Only public posts can be sponsored.");
        if (post.DeletedAt != null)
            return BadRequest("This post is no longer available.");
        if (post.ShadowbanReason is not null and not Shared.Models.PostShadowbanReason.None)
            return BadRequest("This post cannot be sponsored.");

        try
        {
            var (orderId, amount) = await sponsorService.CreateSponsorBidAsync(post, currentUser, request.Amount);
            return Ok(new PostSponsorResponse { OrderId = orderId, Amount = amount });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public class PostPinRequest
    {
        [Required] public Shared.Models.PostPinMode Mode { get; set; }
    }

    [HttpPost("{id:guid}/pin")]
    [Authorize]
    public async Task<ActionResult<SnPost>> PinPost(Guid id, [FromBody] PostPinRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.RepliedPost)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (post.PublisherId == null ||
            !await pub.IsMemberWithRole(post.PublisherId.Value, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You are not an editor of this publisher");

        if (request.Mode == Shared.Models.PostPinMode.RealmPage && post.RealmId != null)
        {
            if (
                !await rs.IsMemberWithRole(
                    post.RealmId.Value,
                    accountId,
                    new List<int> { RealmMemberRole.Moderator }
                )
            )
                return StatusCode(403, "You are not a moderator of this realm");
        }

        try
        {
            await ps.PinPostAsync(post, currentUser, request.Mode);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostPin,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() },
                { "mode", request.Mode.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(post);
    }

    [HttpDelete("{id:guid}/pin")]
    [Authorize]
    public async Task<ActionResult<SnPost>> UnpinPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.RepliedPost)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (post.PublisherId == null ||
            !await pub.IsMemberWithRole(post.PublisherId.Value, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You are not an editor of this publisher");

        if (post is { PinMode: Shared.Models.PostPinMode.RealmPage, RealmId: not null })
        {
            if (
                !await rs.IsMemberWithRole(
                    post.RealmId.Value,
                    accountId,
                    new List<int> { RealmMemberRole.Moderator }
                )
            )
                return StatusCode(403, "You are not a moderator of this realm");
        }

        try
        {
            await ps.UnpinPostAsync(post, currentUser);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostUnpin,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(post);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<SnPost>> UpdatePost(
        Guid id,
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (request.Type != PostType.Blog)
        {
            if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
                return BadRequest("Content is required.");
        }
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) && request.Type != PostType.Article)
            return BadRequest("Thumbnail only supported in article.");
        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) &&
            !(request.Attachments?.Contains(request.ThumbnailId) ?? false))
            return BadRequest("Thumbnail must be presented in attachment list.");
        if (request.DraftedAt is not null && request.PublishedAt is not null)
            return BadRequest("Cannot set both draftedAt and publishedAt.");

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.LockedAt is not null)
            return StatusCode(423, "This post is locked and cannot be edited.");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(post.Publisher!.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to edit this publisher's post.");

        if (pubName is not null)
        {
            var publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null)
                return NotFound();
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return StatusCode(
                    403,
                    "You need at least be an editor to transfer this post to this publisher."
                );
            post.PublisherId = publisher.Id;
            post.Publisher = publisher;
        }

        if (request.Title is not null)
            post.Title = request.Title;
        if (request.Description is not null)
            post.Description = request.Description;
        if (request.Slug is not null)
            post.Slug = request.Slug;
        if (request.Content is not null)
            post.Content = request.Content;
        if (request.Visibility is not null)
            post.Visibility = request.Visibility.Value;

        if (post.Visibility is Shared.Models.PostVisibility.CloseFriendsOnly or Shared.Models.PostVisibility.Friends)
        {
            if (post.PublisherId is null)
                return BadRequest("CloseFriendsOnly and Friends visibility require a publisher.");
        }
        if (request.Type is not null)
            post.Type = request.Type.Value;
        if (request.Language is not null)
            post.Language = request.Language;
        if (request.Meta is not null)
            post.Metadata = request.Meta;
        if (request.DraftedAt is not null)
            post.DraftedAt = request.DraftedAt;

        // Blog post validation on update
        var effectiveType = request.Type ?? post.Type;
        if (effectiveType == PostType.Blog)
        {
            if (!currentUser.IsSuperuser)
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var permissionService = scope.ServiceProvider.GetRequiredService<DyPermissionService.DyPermissionServiceClient>();
                var permResp = await permissionService.HasPermissionAsync(new DyHasPermissionRequest
                {
                    Actor = currentUser.Id,
                    Key = "posts.create.blog"
                });
                if (!permResp.HasPermission)
                    return StatusCode(403, "You do not have permission to create blog posts.");

                // Verify domain if content changed
                if (request.Content is not null)
                {
                    if (!Uri.TryCreate(request.Content, UriKind.Absolute, out var blogUri))
                        return BadRequest("Blog post content must be a valid URL.");

                    var host = blogUri.Host.ToLowerInvariant();
                    var isDomainVerified = await db.PublisherVerifiedDomains
                        .AnyAsync(d => d.PublisherId == post.PublisherId
                            && d.Domain == host
                            && d.Status == DomainVerificationStatus.Verified);
                    if (!isDomainVerified)
                        return StatusCode(403, "This domain is not verified for your publisher.");
                }
            }

            // Update EmbedView to match blog URL
            if (request.Content is not null)
                post.EmbedView = new PostEmbedView { Uri = request.Content, Renderer = PostEmbedViewRenderer.WebView };
        }

        var updateLocationError = TryParseLocation(request.LocationWkt, out var location);
        if (updateLocationError is not null)
            return updateLocationError;

        // The same, this field can be null, so update it anyway.
        if (effectiveType != PostType.Blog)
            post.EmbedView = request.EmbedView;

        // If client provides the complete embeds list, use it directly (replaces all)
        if (request.Embeds is { Count: > 0 })
        {
            post.Metadata ??= new Dictionary<string, object>();
            post.Metadata["embeds"] = request.Embeds;
        }
        else if (request.PollId.HasValue)
        {
            try
            {
                var pollEmbed = await polls.MakePollEmbed(request.PollId.Value);
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                // Remove all old poll embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && type.ToString() == "poll"
                );
                embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            // Remove all old poll embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "poll");
        }

        // Handle fund embeds
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await remotePayments.GetWalletFund(request.FundId.Value.ToString());

                // Check if the fund was created by the current user
                if (fundResponse.CreatorAccountId != currentUser.Id)
                    return BadRequest("You can only share funds that you created.");

                var fundEmbed = new FundEmbed { Id = request.FundId.Value };
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                // Remove all old fund embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && type.ToString() == "fund"
                );
                embeds.Add(EmbeddableBase.ToDictionary(fundEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("The specified fund does not exist.");
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest("Invalid fund ID.");
            }
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            // Remove all old fund embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "fund");
        }

        if (request.MeetId.HasValue)
        {
            var meetEmbed = new MeetEmbed { Id = request.MeetId.Value };
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "meet");
            embeds.Add(EmbeddableBase.ToDictionary(meetEmbed));
            post.Metadata["embeds"] = embeds;
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "meet");
        }

        // Handle live stream embeds
        if (request.LiveStreamId.HasValue)
        {
            try
            {
                var liveStream = await liveStreams.GetByIdAsync(request.LiveStreamId.Value);
                if (liveStream == null)
                    return BadRequest("The specified live stream does not exist.");

                // Check if the live stream belongs to the current user's publisher
                if (liveStream.PublisherId != post.PublisherId)
                    return BadRequest("You can only share live streams from your own publisher.");

                var liveStreamEmbed = new LiveStreamEmbed { Id = request.LiveStreamId.Value };
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                // Remove all old live stream embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && type.ToString() == "livestream"
                );
                embeds.Add(EmbeddableBase.ToDictionary(liveStreamEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest($"Error attaching live stream: {ex.Message}");
            }
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            // Remove all old live stream embeds
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "livestream");
        }

        if (HasLocationPayload(request.LocationName, request.LocationAddress, request.LocationWkt))
        {
            var locationEmbed = CreateLocationEmbed(request.LocationName, request.LocationAddress, location);
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "location");
            embeds.Add(EmbeddableBase.ToDictionary(locationEmbed));
            post.Metadata["embeds"] = embeds;
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "location");
        }

        if (request.NotableDayId.HasValue)
        {
            var notableDayEmbed = new NotableDayEmbed { Id = request.NotableDayId.Value };
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "notable_day");
            embeds.Add(EmbeddableBase.ToDictionary(notableDayEmbed));
            post.Metadata["embeds"] = embeds;
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "notable_day");
        }

        if (request.CalendarEventId.HasValue)
        {
            var calendarEventEmbed = new CalendarEventEmbed { Id = request.CalendarEventId.Value };
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "calendar_event");
            embeds.Add(EmbeddableBase.ToDictionary(calendarEventEmbed));
            post.Metadata["embeds"] = embeds;
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            if (
                !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                || existingEmbeds is not List<EmbeddableBase>
            )
                post.Metadata["embeds"] = new List<Dictionary<string, object>>();
            var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "calendar_event");
        }

        if (request.ThumbnailId is not null)
        {
            post.Metadata ??= new Dictionary<string, object>();
            post.Metadata["thumbnail"] = request.ThumbnailId;
        }
        else
        {
            post.Metadata ??= new Dictionary<string, object>();
            post.Metadata.Remove("thumbnail");
        }

        // The realm is the same as well as the poll
        if (request.RealmId is not null)
        {
            var realm = await rs.GetRealm(request.RealmId.Value.ToString());
            if (
                !await rs.IsMemberWithRole(
                    realm.Id,
                    accountId,
                    new List<int> { RealmMemberRole.Normal }
                )
            )
                return StatusCode(403, "You are not a member of this realm.");
            post.RealmId = realm.Id;
        }
        else
        {
            post.RealmId = null;
        }

        try
        {
            post = await ps.UpdatePostAsync(
                post,
                attachments: request.Attachments,
                tags: request.Tags,
                categories: request.Categories,
                draftedAt: request.DraftedAt,
                publishedAt: request.PublishedAt
            );
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostUpdate,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(post);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<SnPost>> DeletePost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.LockedAt is not null)
            return StatusCode(423, "This post is locked and cannot be deleted.");

        if (
            !await pub.IsMemberWithRole(
                post.Publisher!.Id,
                Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor
            )
        )
            return StatusCode(
                403,
                "You need at least be an editor to delete the publisher's post."
            );

        await ps.DeletePostAsync(post);

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostDelete,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return NoContent();
    }

    [HttpPost("batch/delete")]
    [Authorize]
    public async Task<ActionResult> BatchDeletePosts([FromBody] BatchDeleteRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        if (request.PostIds.Count == 0)
            return BadRequest("PostIds list is empty.");

        var posts = await db
            .Posts.Where(e => request.PostIds.Contains(e.Id))
            .Include(e => e.Publisher)
            .ToListAsync();

        if (posts.Count != request.PostIds.Count)
        {
            var foundIds = posts.Select(p => p.Id).ToHashSet();
            var missingIds = request.PostIds.Where(id => !foundIds.Contains(id)).ToList();
            return BadRequest($"Posts not found: {string.Join(", ", missingIds)}");
        }

        var lockedPost = posts.FirstOrDefault(p => p.LockedAt is not null);
        if (lockedPost is not null)
            return StatusCode(423, $"Post {lockedPost.Id} is locked and cannot be deleted.");

        var accountId = Guid.Parse(currentUser.Id);
        foreach (var postGroup in posts.GroupBy(p => p.Publisher!.Id))
        {
            if (!await pub.IsMemberWithRole(postGroup.Key, accountId, PublisherMemberRole.Editor))
            {
                var pubName = postGroup.First().Publisher?.Name ?? postGroup.Key.ToString();
                return StatusCode(403, $"You need at least be an editor to delete posts from publisher '{pubName}'.");
            }
        }

        foreach (var post in posts)
        {
            await ps.DeletePostAsync(post);

            als.CreateActionLog(
                accountId,
                ActionLogType.PostDelete,
                new Dictionary<string, object>
                {
                    { "post_id", post.Id.ToString() }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );
        }

        return NoContent();
    }

    [HttpPost("batch/visibility")]
    [Authorize]
    public async Task<ActionResult> BatchUpdateVisibility([FromBody] BatchVisibilityRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        if (request.PostIds.Count == 0)
            return BadRequest("PostIds list is empty.");

        if (request.DraftedAt is not null && request.PublishedAt is not null)
            return BadRequest("Cannot set both draftedAt and publishedAt.");

        var posts = await db
            .Posts.Where(e => request.PostIds.Contains(e.Id))
            .Include(e => e.Publisher)
            .ToListAsync();

        if (posts.Count != request.PostIds.Count)
        {
            var foundIds = posts.Select(p => p.Id).ToHashSet();
            var missingIds = request.PostIds.Where(id => !foundIds.Contains(id)).ToList();
            return BadRequest($"Posts not found: {string.Join(", ", missingIds)}");
        }

        var lockedPost = posts.FirstOrDefault(p => p.LockedAt is not null);
        if (lockedPost is not null)
            return StatusCode(423, $"Post {lockedPost.Id} is locked and cannot be edited.");

        var accountId = Guid.Parse(currentUser.Id);
        foreach (var postGroup in posts.GroupBy(p => p.Publisher!.Id))
        {
            if (!await pub.IsMemberWithRole(postGroup.Key, accountId, PublisherMemberRole.Editor))
            {
                var pubName = postGroup.First().Publisher?.Name ?? postGroup.Key.ToString();
                return StatusCode(403, $"You need at least be an editor to edit posts from publisher '{pubName}'.");
            }
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        foreach (var post in posts)
        {
            post.EditedAt = now;

            if (request.Visibility is not null)
                post.Visibility = request.Visibility.Value;

            if (request.PublishedAt is not null)
            {
                if (request.PublishedAt.Value < now && post.DraftedAt is null)
                    return BadRequest($"Cannot set publishedAt to the past for post {post.Id}.");

                post.PublishedAt = request.PublishedAt;
                post.DraftedAt = null;
            }

            if (request.DraftedAt is not null)
            {
                post.DraftedAt = request.DraftedAt;
                post.PublishedAt = null;
            }
        }

        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize]
    [AskPermission("posts.create")]
    public async Task<ActionResult<SnPost>> PublishDraft(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(post.Publisher!.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to publish this post.");

        if (post.DraftedAt is null)
            return BadRequest("This post is not a draft.");

        try
        {
            post = await ps.UpdatePostAsync(
                post,
                publishedAt: Instant.FromDateTimeUtc(DateTime.UtcNow)
            );
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostUpdate,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() },
                { "operation", "publish" }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(post);
    }

    public class BoostRequest
    {
        [MaxLength(1024)] public string? Content { get; set; }
    }

    [HttpPost("{id:guid}/boost")]
    [Authorize]
    [AskPermission("posts.boost")]
    public async Task<ActionResult<SnBoost>> BoostPost(
        Guid id,
        [FromBody] BoostRequest? request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(accountId);

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Actor)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.Publisher?.AccountId != null && post.Publisher.AccountId != accountId)
        {
            if (await IsBlockedEitherDirectionAsync(post.Publisher.AccountId.Value, accountId))
                return BadRequest("You cannot boost this post.");
        }

        SnPublisher? userPublisher = null;
        var settings = await db.PublishingSettings
            .FirstOrDefaultAsync(s => s.AccountId == accountId);
        if (settings?.DefaultFediversePublisherId != null)
        {
            userPublisher = userPublishers.FirstOrDefault(p => p.Id == settings.DefaultFediversePublisherId);
        }

        if (userPublisher == null)
        {
            userPublisher = userPublishers.FirstOrDefault(p => p.AccountId == accountId);
        }

        if (userPublisher is null)
            return BadRequest("You need a publisher to boost posts");

        var existingBoost = await db.Boosts
            .FirstOrDefaultAsync(b => b.PostId == post.Id && b.Actor.PublisherId == userPublisher.Id);

        if (existingBoost != null)
            return BadRequest("You have already boosted this post");

        var localActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == userPublisher.Id);

        if (localActor is null)
            return BadRequest("Publisher does not have an ActivityPub actor");

        var boost = new SnBoost
        {
            PostId = post.Id,
            ActorId = localActor.Id,
            Content = request?.Content,
            BoostedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.Boosts.Add(boost);
        post.BoostCount++;
        await db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                await ps.NotifyPostForwardSubscribersAsync(post, userPublisher, accountId);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error when sending subscribed post forward notifications for post {PostId}",
                    post.Id
                );
            }
        });

        await activityPubDelivery.SendAnnounceActivityAsync(post, localActor, request?.Content);

        als.CreateActionLog(
            accountId,
            ActionLogType.PostBoost,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(boost);
    }

    [HttpDelete("{id:guid}/boost")]
    [Authorize]
    public async Task<IActionResult> UnboostPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var userPublishers = await pub.GetUserPublishers(accountId);
        SnPublisher? userPublisher = null;
        var settings = await db.PublishingSettings
            .FirstOrDefaultAsync(s => s.AccountId == accountId);
        if (settings?.DefaultFediversePublisherId != null)
        {
            userPublisher = userPublishers.FirstOrDefault(p => p.Id == settings.DefaultFediversePublisherId);
        }

        if (userPublisher == null)
        {
            userPublisher = userPublishers.FirstOrDefault(p => p.AccountId == accountId);
        }

        if (userPublisher is null)
            return BadRequest("You need a publisher to unboost posts");

        var localActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == userPublisher.Id);

        if (localActor is null)
            return BadRequest("Publisher does not have an ActivityPub actor");

        var boost = await db.Boosts
            .FirstOrDefaultAsync(b => b.PostId == id && b.ActorId == localActor.Id);

        if (boost is null)
            return NotFound();

        var post = await db.Posts.FindAsync(id);
        if (post != null)
        {
            post.BoostCount = Math.Max(0, post.BoostCount - 1);
        }

        db.Boosts.Remove(boost);
        await db.SaveChangesAsync();

        if (post != null)
        {
            await activityPubDelivery.SendUndoAnnounceActivityAsync(post, localActor);
        }

        als.CreateActionLog(
            accountId,
            ActionLogType.PostUnboost,
            new Dictionary<string, object>
            {
                { "post_id", id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return NoContent();
    }

    [HttpGet("{id:guid}/boosts")]
    public async Task<ActionResult<List<SnBoost>>> GetPostBoosts(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var query = db.Boosts.Where(b => b.PostId == id);
        var totalCount = await query.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var boosts = await query
            .Include(b => b.Actor)
            .OrderByDescending(b => b.BoostedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(boosts);
    }

    [HttpPost("{id:guid}/realm/moderate")]
    [Authorize]
    [AskPermission("posts.moderate")]
    public async Task<ActionResult> ModeratePostInRealm(Guid id, [FromBody] ModeratePostRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var post = await db.Posts
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (post is null)
            return NotFound();

        if (post.RealmId is null)
            return BadRequest("This post is not linked to a realm.");

        var accountId = Guid.Parse(currentUser.Id);

        // Check if user has permission to moderate posts in this realm
        if (!await rs.HasPermission(post.RealmId.Value, accountId, "post.moderate"))
            return StatusCode(403, "You do not have permission to moderate posts in this realm.");

        // Check if post is already moderated
        if (await db.RealmPostModerationLogs.AnyAsync(l => l.PostId == post.Id && l.RealmId == post.RealmId.Value && l.DeletedAt == null))
            return BadRequest("This post has already been removed from the realm.");

        // Create moderation log
        var moderationLog = new SnRealmPostModerationLog
        {
            RealmId = post.RealmId.Value,
            PostId = post.Id,
            ModeratorAccountId = accountId,
            Reason = request.Reason
        };
        db.RealmPostModerationLogs.Add(moderationLog);
        await db.SaveChangesAsync();

        // Remove post from realm
        var result = await ps.RemovePostFromRealmAsync(post, accountId, request.Reason);

        als.CreateActionLog(
            accountId,
            ActionLogType.PostModerate,
            new Dictionary<string, object>
            {
                { "post_id", post.Id.ToString() },
                { "realm_id", post.RealmId.ToString() ?? "" },
                { "reason", request.Reason ?? "" }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(new
        {
            success = true,
            message = "Post removed from realm",
            post = result
        });
    }

    public class ModeratePostRequest
    {
        [MaxLength(4096)] public string? Reason { get; set; }
    }
}
