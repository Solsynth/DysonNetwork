using System.ComponentModel.DataAnnotations;
using System.Globalization;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Models;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.Reader;
using DysonNetwork.Sphere.Survey;
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
using SurveysService = DysonNetwork.Sphere.Survey.SurveyService;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
[ApiFeature("posts.publish", Revision = 1)]
[ApiFeature("posts.reactions", Revision = 1)]
[ApiFeature("posts.bookmark", Revision = 1)]
[ApiFeature("posts.awards", Revision = 1)]
[ApiFeature("posts.sponsor", Revision = 1)]
[ApiFeature("posts.pin", Revision = 1)]
[ApiFeature("posts.boost", Revision = 1)]
[ApiFeature("posts.moderation", Revision = 1)]
public class PostActionController(
    AppDatabase db,
    PostService ps,
    PublisherService pub,
    PostCollectionService pcs,
    DyProfileService.DyProfileServiceClient accounts,
    RemoteActionLogService als,
    RemotePaymentService remotePayments,
    SurveysService surveys,
    RemoteRealmService rs,
    LiveStreamService liveStreams,
    WebReaderService webReader,
    ActivityPubDeliveryService activityPubDelivery,
    SponsorService sponsorService,
    ILogger<PostActionController> logger,
    IEventBus eventBus
) : ControllerBase
{
    private static string? NormalizeOptionalText(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

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
            return BadRequest(new ApiError { Code = "POST_INVALID_LOCATION", Message = "Invalid location WKT.", Status = 400 });
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

    private async Task<ActionResult?> PopulateBlogMetadataAsync(SnPost post)
    {
        if (post.Type != PostType.Blog)
            return null;
        if (string.IsNullOrWhiteSpace(post.Content))
            return BadRequest(new ApiError { Code = "POST_BLOG_INVALID_URL", Message = "Blog post content must be a valid URL.", Status = 400 });

        try
        {
            var linkEmbed = await webReader.GetLinkPreviewAsync(post.Content);

            if (string.IsNullOrWhiteSpace(post.Title))
                post.Title = NormalizeOptionalText(linkEmbed.Title);
            if (string.IsNullOrWhiteSpace(post.Description))
                post.Description = NormalizeOptionalText(linkEmbed.Description);

            post.Metadata ??= new Dictionary<string, object>();

            List<Dictionary<string, object>> embeds;
            if (post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                && existingEmbeds is List<Dictionary<string, object>> existingEmbedList)
            {
                embeds = existingEmbedList;
            }
            else
            {
                embeds = [];
                post.Metadata["embeds"] = embeds;
            }

            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == "link");
            embeds.Add(EmbeddableBase.ToDictionary(linkEmbed));
            post.Metadata["embeds"] = embeds;

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch blog preview for URL {Url}", post.Content);
            return BadRequest(new ApiError { Code = "POST_BLOG_PREVIEW_FAILED", Message = "Unable to fetch a link preview for this blog URL.", Status = 400 });
        }
    }

    private sealed record BlogPermissionCheckResult(
        bool PermissionNodeGranted,
        bool PermissionGranted,
        bool DomainVerified,
        string Domain,
        SnPublisher Publisher
    )
    {
        public bool CanCreate => PermissionGranted && DomainVerified;
    }

    private async Task<SnPublisher?> ResolvePublisherForPostRequestAsync(Guid accountId, string? pubName, bool isReply)
    {
        if (pubName is not null)
            return await pub.GetPublisherByName(pubName);

        var settings = await db.PublishingSettings.FirstOrDefaultAsync(s => s.AccountId == accountId);
        var defaultPublisherId = isReply
            ? settings?.DefaultReplyPublisherId
            : settings?.DefaultPostingPublisherId;

        if (defaultPublisherId != null)
        {
            var defaultPublisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == defaultPublisherId);
            if (defaultPublisher != null && await pub.IsMemberWithRole(defaultPublisher.Id, accountId, PublisherMemberRole.Editor))
                return defaultPublisher;
        }

        return await db.Publishers.FirstOrDefaultAsync(e =>
            e.AccountId == accountId && e.Type == Shared.Models.PublisherType.Individual
        );
    }

    private async Task<BlogPermissionCheckResult> CheckBlogCreatePermissionAsync(
        DyAccount currentUser,
        SnPublisher publisher,
        string url
    )
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var blogUri))
            throw new InvalidOperationException("Blog post content must be a valid URL.");

        using var scope = HttpContext.RequestServices.CreateScope();
        var permissionService = scope.ServiceProvider.GetRequiredService<DyPermissionService.DyPermissionServiceClient>();
        var permissionResponse = await permissionService.HasPermissionAsync(new DyHasPermissionRequest
        {
            Actor = currentUser.Id,
            Key = "posts.create.blog"
        });

        var host = blogUri.Host.ToLowerInvariant();
        var isDomainVerified = await db.PublisherVerifiedDomains
            .AnyAsync(d => d.PublisherId == publisher.Id
                && d.Domain == host
                && d.Status == DomainVerificationStatus.Verified);

        return new BlogPermissionCheckResult(
            PermissionNodeGranted: permissionResponse.HasPermission,
            PermissionGranted: currentUser.IsSuperuser || permissionResponse.HasPermission,
            DomainVerified: isDomainVerified,
            Domain: host,
            Publisher: publisher
        );
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

        public Guid? SurveyId { get; set; }
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

    public class BlogPermissionCheckRequest
    {
        [Required]
        public string Url { get; set; } = null!;
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

    [HttpPost("blog/check-permission")]
    [Authorize]
    public async Task<ActionResult> CheckBlogCreatePermission(
        [FromBody] BlogPermissionCheckRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var normalizedUrl = NormalizeOptionalText(request.Url);
        if (normalizedUrl is null)
            return BadRequest(new ApiError { Code = "POST_URL_REQUIRED", Message = "URL is required.", Status = 400 });

        var accountId = Guid.Parse(currentUser.Id);
        var publisher = await ResolvePublisherForPostRequestAsync(accountId, pubName, isReply: false);
        if (publisher is null)
            return BadRequest(new ApiError { Code = "PUBLISHER_NOT_FOUND", Message = "Publisher was not found.", Status = 400 });

        if (pubName is not null && !await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, ApiError.Unauthorized("You need at least be an editor to post as this publisher.", forbidden: true));

        BlogPermissionCheckResult result;
        try
        {
            result = await CheckBlogCreatePermissionAsync(currentUser, publisher, normalizedUrl);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "POST_BLOG_PERMISSION_CHECK_FAILED", Message = ex.Message, Status = 400 });
        }

        return Ok(new
        {
            can_create = result.CanCreate,
            permission_node_granted = result.PermissionNodeGranted,
            permission_granted = result.PermissionGranted,
            domain_verified = result.DomainVerified,
            domain = result.Domain,
            publisher_id = result.Publisher.Id,
            publisher_name = result.Publisher.Name
        });
    }

    [HttpPost]
    [AskPermission("posts.create")]
    public async Task<ActionResult<SnPost>> CreatePost(
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        request.Title = NormalizeOptionalText(request.Title);
        request.Description = NormalizeOptionalText(request.Description);
        if (request.Type != PostType.Blog)
        {
            if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
                return BadRequest(new ApiError { Code = "POST_CONTENT_REQUIRED", Message = "Content is required.", Status = 400 });
        }
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
                    return StatusCode(403, ApiError.Unauthorized("You do not have permission to create blog posts.", forbidden: true));
            }

            if (string.IsNullOrWhiteSpace(request.Content) || !Uri.TryCreate(request.Content, UriKind.Absolute, out _))
                return BadRequest(new ApiError { Code = "POST_BLOG_INVALID_URL", Message = "Blog post content must be a valid URL.", Status = 400 });
        }

        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) && request.Type != PostType.Article)
            return BadRequest(new ApiError { Code = "POST_THUMBNAIL_NOT_SUPPORTED", Message = "Thumbnail only supported in article.", Status = 400 });
        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) &&
            !(request.Attachments?.Contains(request.ThumbnailId) ?? false))
            return BadRequest(new ApiError { Code = "POST_THUMBNAIL_NOT_IN_ATTACHMENTS", Message = "Thumbnail must be presented in attachment list.", Status = 400 });
        if (request.DraftedAt is not null && request.PublishedAt is not null)
            return BadRequest(new ApiError { Code = "POST_DRAFT_PUBLISH_CONFLICT", Message = "Cannot set both draftedAt and publishedAt.", Status = 400 });

        var locationError = TryParseLocation(request.LocationWkt, out var location);
        if (locationError is not null)
            return locationError;

        var accountId = Guid.Parse(currentUser.Id);
        var publisher = await ResolvePublisherForPostRequestAsync(accountId, pubName, request.RepliedPostId != null);

        if (publisher is null)
            return BadRequest(new ApiError { Code = "PUBLISHER_NOT_FOUND", Message = "Publisher was not found.", Status = 400 });
        if (pubName is not null && !await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, ApiError.Unauthorized("You need at least be an editor to post as this publisher.", forbidden: true));

        if (request.Type == PostType.Blog)
        {
            BlogPermissionCheckResult blogPermission;
            try
            {
                blogPermission = await CheckBlogCreatePermissionAsync(currentUser, publisher, request.Content!);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ApiError { Code = "POST_BLOG_PERMISSION_CHECK_FAILED", Message = ex.Message, Status = 400 });
            }

            if (!blogPermission.PermissionGranted)
                return StatusCode(403, ApiError.Unauthorized("You do not have permission to create blog posts.", forbidden: true));
            if (!blogPermission.DomainVerified)
                return StatusCode(403, ApiError.Unauthorized("This domain is not verified for your publisher. Add it via the domains endpoint first.", forbidden: true));
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
            EmbedView = request.EmbedView,
            Language = request.Language,
            Publisher = publisher,
        };

        if (post.Visibility is Shared.Models.PostVisibility.CloseFriendsOnly or Shared.Models.PostVisibility.Friends)
        {
            if (publisher is null)
                return BadRequest(new ApiError { Code = "POST_VISIBILITY_REQUIRES_PUBLISHER", Message = "CloseFriendsOnly and Friends visibility require a publisher.", Status = 400 });
        }

        if (request.RepliedPostId is not null)
        {
            var repliedPost = await db
                .Posts.Where(p => p.Id == request.RepliedPostId.Value)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();
            if (repliedPost is null)
                return BadRequest(new ApiError { Code = "POST_REPLY_TARGET_NOT_FOUND", Message = "Post replying to was not found.", Status = 400 });

            if (repliedPost.Publisher?.AccountId != null)
            {
                if (await IsBlockedEitherDirectionAsync(repliedPost.Publisher.AccountId.Value, accountId))
                    return BadRequest(new ApiError { Code = "POST_REPLY_BLOCKED", Message = "You cannot reply to a post from a user who blocked you.", Status = 400 });
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
                return BadRequest(new ApiError { Code = "POST_FORWARD_TARGET_NOT_FOUND", Message = "Forwarded post was not found.", Status = 400 });
            if (forwardedPost.Publisher?.AccountId != null && forwardedPost.Publisher.AccountId != accountId)
            {
                if (await IsBlockedEitherDirectionAsync(forwardedPost.Publisher.AccountId.Value, accountId))
                    return BadRequest(new ApiError { Code = "POST_FORWARD_BLOCKED", Message = "You cannot forward a post from a blocked user.", Status = 400 });
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
                return StatusCode(403, ApiError.Unauthorized("You are not a member of this realm.", forbidden: true));
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
        else if (request.SurveyId.HasValue)
        {
            try
            {
                var surveyEmbed = await surveys.MakeSurveyEmbed(request.SurveyId.Value);
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                // Remove all old poll embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && (type.ToString() == "survey" || type.ToString() == "poll")
                );
                embeds.Add(EmbeddableBase.ToDictionary(surveyEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiError { Code = "POST_SURVEY_EMBED_FAILED", Message = ex.Message, Status = 400 });
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
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && (type.ToString() == "survey" || type.ToString() == "poll"));
        }

        // Handle fund embeds
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await remotePayments.GetWalletFund(request.FundId.Value.ToString());

                // Check if the fund was created by the current user
                if (fundResponse.CreatorAccountId != currentUser.Id)
                    return BadRequest(new ApiError { Code = "POST_FUND_NOT_OWNER", Message = "You can only share funds that you created.", Status = 400 });

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
                return BadRequest(new ApiError { Code = "POST_FUND_NOT_FOUND", Message = "The specified fund does not exist.", Status = 400 });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest(new ApiError { Code = "POST_FUND_INVALID_ID", Message = "Invalid fund ID.", Status = 400 });
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
                    return BadRequest(new ApiError { Code = "POST_LIVE_STREAM_NOT_FOUND", Message = "The specified live stream does not exist.", Status = 400 });

                // Check if the live stream belongs to the current user's publisher
                if (liveStream.PublisherId != publisher.Id)
                    return BadRequest(new ApiError { Code = "POST_LIVE_STREAM_NOT_OWNER", Message = "You can only share live streams from your own publisher.", Status = 400 });

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
                return BadRequest(new ApiError { Code = "POST_LIVE_STREAM_EMBED_FAILED", Message = $"Error attaching live stream: {ex.Message}", Status = 400 });
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

        var blogMetadataError = await PopulateBlogMetadataAsync(post);
        if (blogMetadataError is not null)
            return blogMetadataError;

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
            return BadRequest(new ApiError { Code = "POST_CREATE_FAILED", Message = err.Message, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        );
        var userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        if (!ReactionsAllowedDefault.Contains(request.Symbol))
            if (currentUser.PerkSubscription is null)
                return BadRequest(new ApiError { Code = "POST_REACTION_SUBSCRIPTION_REQUIRED", Message = "You need subscription to send custom reactions", Status = 400 });

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
                return BadRequest(new ApiError { Code = "POST_REACTION_BLOCKED", Message = "You cannot react to this post.", Status = 400 });
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
    [AskPermission(PermissionKeys.PostsBookmark)]
    [Authorize]
    public async Task<ActionResult<SnPostBookmark>> BookmarkPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
    [AskPermission(PermissionKeys.PostsBookmark)]
    [Authorize]
    public async Task<ActionResult> UnbookmarkPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
    [AskPermission(PermissionKeys.PostsAward)]
    [Authorize]
    public async Task<ActionResult<PostAwardResponse>> AwardPost(
        Guid id,
        [FromBody] PostAwardRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        if (request.Attitude == PostReactionAttitude.Neutral)
            return BadRequest(new ApiError { Code = "POST_AWARD_NEUTRAL_NOT_ALLOWED", Message = "You cannot create a neutral post award", Status = 400 });

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
                return BadRequest(new ApiError { Code = "POST_AWARD_BLOCKED", Message = "You cannot award this post.", Status = 400 });
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
    [AskPermission(PermissionKeys.PostsSponsor)]
    [Authorize]
    public async Task<ActionResult<PostSponsorResponse>> SponsorPost(
        Guid id,
        [FromBody] PostSponsorRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        if (request.Amount < SponsorService.MinimumBidAmount)
            return BadRequest(new ApiError { Code = "POST_SPONSOR_MINIMUM_BID", Message = $"Minimum sponsorship bid is {SponsorService.MinimumBidAmount} golds.", Status = 400 });

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();
        if (post.Visibility != Shared.Models.PostVisibility.Public)
            return BadRequest(new ApiError { Code = "POST_SPONSOR_NOT_PUBLIC", Message = "Only public posts can be sponsored.", Status = 400 });
        if (post.DeletedAt != null)
            return BadRequest(new ApiError { Code = "POST_SPONSOR_DELETED", Message = "This post is no longer available.", Status = 400 });
        if (post.ShadowbanReason is not null and not Shared.Models.PostShadowbanReason.None)
            return BadRequest(new ApiError { Code = "POST_SPONSOR_SHADOWBANNED", Message = "This post cannot be sponsored.", Status = 400 });

        try
        {
            var (orderId, amount) = await sponsorService.CreateSponsorBidAsync(post, currentUser, request.Amount);
            return Ok(new PostSponsorResponse { OrderId = orderId, Amount = amount });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "POST_SPONSOR_CREATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    public class PostPinRequest
    {
        [Required] public Shared.Models.PostPinMode Mode { get; set; }
    }

    [HttpPost("{id:guid}/pin")]
    [AskPermission(PermissionKeys.PostsPin)]
    [Authorize]
    public async Task<ActionResult<SnPost>> PinPost(Guid id, [FromBody] PostPinRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
            return StatusCode(403, ApiError.Unauthorized("You are not an editor of this publisher", forbidden: true));

        if (request.Mode == Shared.Models.PostPinMode.RealmPage && post.RealmId != null)
        {
            if (
                !await rs.IsMemberWithRole(
                    post.RealmId.Value,
                    accountId,
                    new List<int> { RealmMemberRole.Moderator }
                )
            )
                return StatusCode(403, ApiError.Unauthorized("You are not a moderator of this realm", forbidden: true));
        }

        try
        {
            await ps.PinPostAsync(post, currentUser, request.Mode);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "POST_PIN_FAILED", Message = err.Message, Status = 400 });
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
    [AskPermission(PermissionKeys.PostsPin)]
    [Authorize]
    public async Task<ActionResult<SnPost>> UnpinPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
            return StatusCode(403, ApiError.Unauthorized("You are not an editor of this publisher", forbidden: true));

        if (post is { PinMode: Shared.Models.PostPinMode.RealmPage, RealmId: not null })
        {
            if (
                !await rs.IsMemberWithRole(
                    post.RealmId.Value,
                    accountId,
                    new List<int> { RealmMemberRole.Moderator }
                )
            )
                return StatusCode(403, ApiError.Unauthorized("You are not a moderator of this realm", forbidden: true));
        }

        try
        {
            await ps.UnpinPostAsync(post, currentUser);
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "POST_UNPIN_FAILED", Message = err.Message, Status = 400 });
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
        request.Title = NormalizeOptionalText(request.Title);
        request.Description = NormalizeOptionalText(request.Description);
        if (request.Type != PostType.Blog)
        {
            if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
                return BadRequest(new ApiError { Code = "POST_CONTENT_REQUIRED", Message = "Content is required.", Status = 400 });
        }
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) && request.Type != PostType.Article)
            return BadRequest(new ApiError { Code = "POST_THUMBNAIL_NOT_SUPPORTED", Message = "Thumbnail only supported in article.", Status = 400 });
        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) &&
            !(request.Attachments?.Contains(request.ThumbnailId) ?? false))
            return BadRequest(new ApiError { Code = "POST_THUMBNAIL_NOT_IN_ATTACHMENTS", Message = "Thumbnail must be presented in attachment list.", Status = 400 });
        if (request.DraftedAt is not null && request.PublishedAt is not null)
            return BadRequest(new ApiError { Code = "POST_DRAFT_PUBLISH_CONFLICT", Message = "Cannot set both draftedAt and publishedAt.", Status = 400 });

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
            return StatusCode(423, ApiError.WithStatus(423, "This post is locked and cannot be edited.", code: "POST_LOCKED"));

        var accountId = Guid.Parse(currentUser.Id);
        if (!await pub.IsMemberWithRole(post.Publisher!.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, ApiError.Unauthorized("You need at least be an editor to edit this publisher's post.", forbidden: true));

        if (pubName is not null)
        {
            var publisher = await pub.GetPublisherByName(pubName);
            if (publisher is null)
                return NotFound();
            if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
                return StatusCode(
                    403,
                    ApiError.Unauthorized("You need at least be an editor to transfer this post to this publisher.", forbidden: true)
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
                return BadRequest(new ApiError { Code = "POST_VISIBILITY_REQUIRES_PUBLISHER", Message = "CloseFriendsOnly and Friends visibility require a publisher.", Status = 400 });
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
            try
            {
                var blogPermission = await CheckBlogCreatePermissionAsync(currentUser, post.Publisher!, post.Content!);
                if (!blogPermission.PermissionGranted)
                    return StatusCode(403, ApiError.Unauthorized("You do not have permission to create blog posts.", forbidden: true));
                if (!blogPermission.DomainVerified)
                    return StatusCode(403, ApiError.Unauthorized("This domain is not verified for your publisher.", forbidden: true));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ApiError { Code = "POST_BLOG_PERMISSION_CHECK_FAILED", Message = ex.Message, Status = 400 });
            }
        }

        var updateLocationError = TryParseLocation(request.LocationWkt, out var location);
        if (updateLocationError is not null)
            return updateLocationError;

        // The same, this field can be null, so update it anyway.
        post.EmbedView = request.EmbedView;

        // If client provides the complete embeds list, use it directly (replaces all)
        if (request.Embeds is { Count: > 0 })
        {
            post.Metadata ??= new Dictionary<string, object>();
            post.Metadata["embeds"] = request.Embeds;
        }
        else if (request.SurveyId.HasValue)
        {
            try
            {
                var surveyEmbed = await surveys.MakeSurveyEmbed(request.SurveyId.Value);
                post.Metadata ??= new Dictionary<string, object>();
                if (
                    !post.Metadata.TryGetValue("embeds", out var existingEmbeds)
                    || existingEmbeds is not List<EmbeddableBase>
                )
                    post.Metadata["embeds"] = new List<Dictionary<string, object>>();
                var embeds = (List<Dictionary<string, object>>)post.Metadata["embeds"];
                // Remove all old poll embeds
                embeds.RemoveAll(e =>
                    e.TryGetValue("type", out var type) && (type.ToString() == "survey" || type.ToString() == "poll")
                );
                embeds.Add(EmbeddableBase.ToDictionary(surveyEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest(new ApiError { Code = "POST_SURVEY_EMBED_FAILED", Message = ex.Message, Status = 400 });
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
            embeds.RemoveAll(e => e.TryGetValue("type", out var type) && (type.ToString() == "survey" || type.ToString() == "poll"));
        }

        // Handle fund embeds
        if (request.FundId.HasValue)
        {
            try
            {
                var fundResponse = await remotePayments.GetWalletFund(request.FundId.Value.ToString());

                // Check if the fund was created by the current user
                if (fundResponse.CreatorAccountId != currentUser.Id)
                    return BadRequest(new ApiError { Code = "POST_FUND_NOT_OWNER", Message = "You can only share funds that you created.", Status = 400 });

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
                return BadRequest(new ApiError { Code = "POST_FUND_NOT_FOUND", Message = "The specified fund does not exist.", Status = 400 });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.InvalidArgument)
            {
                return BadRequest(new ApiError { Code = "POST_FUND_INVALID_ID", Message = "Invalid fund ID.", Status = 400 });
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
                    return BadRequest(new ApiError { Code = "POST_LIVE_STREAM_NOT_FOUND", Message = "The specified live stream does not exist.", Status = 400 });

                // Check if the live stream belongs to the current user's publisher
                if (liveStream.PublisherId != post.PublisherId)
                    return BadRequest(new ApiError { Code = "POST_LIVE_STREAM_NOT_OWNER", Message = "You can only share live streams from your own publisher.", Status = 400 });

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
                return BadRequest(new ApiError { Code = "POST_LIVE_STREAM_EMBED_FAILED", Message = $"Error attaching live stream: {ex.Message}", Status = 400 });
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
                return StatusCode(403, ApiError.Unauthorized("You are not a member of this realm.", forbidden: true));
            post.RealmId = realm.Id;
        }
        else
        {
            post.RealmId = null;
        }

        var updateBlogMetadataError = await PopulateBlogMetadataAsync(post);
        if (updateBlogMetadataError is not null)
            return updateBlogMetadataError;

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
            return BadRequest(new ApiError { Code = "POST_PUBLISH_FAILED", Message = err.Message, Status = 400 });
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.LockedAt is not null)
            return StatusCode(423, ApiError.WithStatus(423, "This post is locked and cannot be deleted.", code: "POST_LOCKED"));

        if (
            !await pub.IsMemberWithRole(
                post.Publisher!.Id,
                Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor
            )
        )
            return StatusCode(
                403,
                ApiError.Unauthorized("You need at least be an editor to delete the publisher's post.", forbidden: true)
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
    [AskPermission(PermissionKeys.PostsBatchDelete)]
    [Authorize]
    public async Task<ActionResult> BatchDeletePosts([FromBody] BatchDeleteRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (request.PostIds.Count == 0)
            return BadRequest(new ApiError { Code = "POST_BATCH_EMPTY", Message = "PostIds list is empty.", Status = 400 });

        var posts = await db
            .Posts.Where(e => request.PostIds.Contains(e.Id))
            .Include(e => e.Publisher)
            .ToListAsync();

        if (posts.Count != request.PostIds.Count)
        {
            var foundIds = posts.Select(p => p.Id).ToHashSet();
            var missingIds = request.PostIds.Where(id => !foundIds.Contains(id)).ToList();
            return BadRequest(new ApiError { Code = "POST_BATCH_NOT_FOUND", Message = $"Posts not found: {string.Join(", ", missingIds)}", Status = 400 });
        }

        var lockedPost = posts.FirstOrDefault(p => p.LockedAt is not null);
        if (lockedPost is not null)
            return StatusCode(423, ApiError.WithStatus(423, $"Post {lockedPost.Id} is locked and cannot be deleted.", code: "POST_LOCKED"));

        var accountId = Guid.Parse(currentUser.Id);
        foreach (var postGroup in posts.GroupBy(p => p.Publisher!.Id))
        {
            if (!await pub.IsMemberWithRole(postGroup.Key, accountId, PublisherMemberRole.Editor))
            {
                var pubName = postGroup.First().Publisher?.Name ?? postGroup.Key.ToString();
                return StatusCode(403, ApiError.Unauthorized($"You need at least be an editor to delete posts from publisher '{pubName}'.", forbidden: true));
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
    [AskPermission(PermissionKeys.PostsBatchDelete)]
    [Authorize]
    public async Task<ActionResult> BatchUpdateVisibility([FromBody] BatchVisibilityRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (request.PostIds.Count == 0)
            return BadRequest(new ApiError { Code = "POST_BATCH_EMPTY", Message = "PostIds list is empty.", Status = 400 });

        if (request.DraftedAt is not null && request.PublishedAt is not null)
            return BadRequest(new ApiError { Code = "POST_DRAFT_PUBLISH_CONFLICT", Message = "Cannot set both draftedAt and publishedAt.", Status = 400 });

        var posts = await db
            .Posts.Where(e => request.PostIds.Contains(e.Id))
            .Include(e => e.Publisher)
            .ToListAsync();

        if (posts.Count != request.PostIds.Count)
        {
            var foundIds = posts.Select(p => p.Id).ToHashSet();
            var missingIds = request.PostIds.Where(id => !foundIds.Contains(id)).ToList();
            return BadRequest(new ApiError { Code = "POST_BATCH_NOT_FOUND", Message = $"Posts not found: {string.Join(", ", missingIds)}", Status = 400 });
        }

        var lockedPost = posts.FirstOrDefault(p => p.LockedAt is not null);
        if (lockedPost is not null)
            return StatusCode(423, ApiError.WithStatus(423, $"Post {lockedPost.Id} is locked and cannot be edited.", code: "POST_LOCKED"));

        var accountId = Guid.Parse(currentUser.Id);
        foreach (var postGroup in posts.GroupBy(p => p.Publisher!.Id))
        {
            if (!await pub.IsMemberWithRole(postGroup.Key, accountId, PublisherMemberRole.Editor))
            {
                var pubName = postGroup.First().Publisher?.Name ?? postGroup.Key.ToString();
                return StatusCode(403, ApiError.Unauthorized($"You need at least be an editor to edit posts from publisher '{pubName}'.", forbidden: true));
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
                    return BadRequest(new ApiError { Code = "POST_PUBLISH_PAST_NOT_ALLOWED", Message = $"Cannot set publishedAt to the past for post {post.Id}.", Status = 400 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
            return StatusCode(403, ApiError.Unauthorized("You need at least be an editor to publish this post.", forbidden: true));

        if (post.DraftedAt is null)
            return BadRequest(new ApiError { Code = "POST_NOT_A_DRAFT", Message = "This post is not a draft.", Status = 400 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
                return BadRequest(new ApiError { Code = "POST_BOOST_BLOCKED", Message = "You cannot boost this post.", Status = 400 });
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
            return BadRequest(new ApiError { Code = "POST_BOOST_PUBLISHER_REQUIRED", Message = "You need a publisher to boost posts.", Status = 400 });

        var existingBoost = await db.Boosts
            .FirstOrDefaultAsync(b => b.PostId == post.Id && b.Actor.PublisherId == userPublisher.Id);

        if (existingBoost != null)
            return BadRequest(new ApiError { Code = "POST_ALREADY_BOOSTED", Message = "You have already boosted this post.", Status = 400 });

        var localActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == userPublisher.Id);

        if (localActor is null)
            return BadRequest(new ApiError { Code = "POST_BOOST_ACTOR_NOT_FOUND", Message = "Publisher does not have an ActivityPub actor.", Status = 400 });

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
    [AskPermission(PermissionKeys.PostsBoost)]
    [Authorize]
    public async Task<IActionResult> UnboostPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
            return BadRequest(new ApiError { Code = "POST_UNBOOST_PUBLISHER_REQUIRED", Message = "You need a publisher to unboost posts.", Status = 400 });

        var localActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == userPublisher.Id);

        if (localActor is null)
            return BadRequest(new ApiError { Code = "POST_UNBOOST_ACTOR_NOT_FOUND", Message = "Publisher does not have an ActivityPub actor.", Status = 400 });

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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var post = await db.Posts
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (post is null)
            return NotFound();

        if (post.RealmId is null)
            return BadRequest(new ApiError { Code = "POST_NOT_IN_REALM", Message = "This post is not linked to a realm.", Status = 400 });

        var accountId = Guid.Parse(currentUser.Id);

        // Check if user has permission to moderate posts in this realm
        if (!await rs.HasPermission(post.RealmId.Value, accountId, "post.moderate"))
            return StatusCode(403, ApiError.Unauthorized("You do not have permission to moderate posts in this realm.", forbidden: true));

        // Check if post is already moderated
        if (await db.RealmPostModerationLogs.AnyAsync(l => l.PostId == post.Id && l.RealmId == post.RealmId.Value && l.DeletedAt == null))
            return BadRequest(new ApiError { Code = "POST_ALREADY_REMOVED_FROM_REALM", Message = "This post has already been removed from the realm.", Status = 400 });

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
