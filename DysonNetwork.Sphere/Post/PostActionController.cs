using System.ComponentModel.DataAnnotations;
using System.Globalization;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Wallet;

using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    AccountService.AccountServiceClient accounts,
    ActionLogService.ActionLogServiceClient als,
    RemotePaymentService remotePayments,
    PollsService polls,
    RemoteRealmService rs
) : ControllerBase
{
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
        public Instant? PublishedAt { get; set; }
        public Guid? RepliedPostId { get; set; }
        public Guid? ForwardedPostId { get; set; }
        public Guid? RealmId { get; set; }

        public Guid? PollId { get; set; }
        public Guid? FundId { get; set; }
        public string? ThumbnailId { get; set; }
    }

    [HttpPost]
    [AskPermission("posts.create")]
    public async Task<ActionResult<SnPost>> CreatePost(
        [FromBody] PostRequest request,
        [FromQuery(Name = "pub")] string? pubName
    )
    {
        request.Content = TextSanitizer.Sanitize(request.Content);
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) && request.Type != PostType.Article)
            return BadRequest("Thumbnail only supported in article.");
        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) &&
            !(request.Attachments?.Contains(request.ThumbnailId) ?? false))
            return BadRequest("Thumbnail must be presented in attachment list.");

        var accountId = Guid.Parse(currentUser.Id);

        SnPublisher? publisher;
        if (pubName is null)
        {
            // Use the first personal publisher
            publisher = await db.Publishers.FirstOrDefaultAsync(e =>
                e.AccountId == accountId && e.Type == Shared.Models.PublisherType.Individual
            );
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

        var post = new SnPost
        {
            Title = request.Title,
            Description = request.Description,
            Slug = request.Slug,
            Content = request.Content,
            Visibility = request.Visibility ?? Shared.Models.PostVisibility.Public,
            PublishedAt = request.PublishedAt,
            Type = request.Type ?? PostType.Moment,
            Metadata = request.Meta,
            EmbedView = request.EmbedView,
            Publisher = publisher,
        };

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
                var relationship = await accounts.GetRelationshipAsync(new GetRelationshipRequest
                {
                    AccountId = repliedPost.Publisher.AccountId.ToString(),
                    RelatedId = accountId.ToString(),
                });
                if (relationship.Relationship is not null && relationship.Relationship.Status <= -100) return BadRequest("You cannot reply who blocked you.");
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

        if (request.PollId.HasValue)
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
                embeds.Add(EmbeddableBase.ToDictionary(pollEmbed));
                post.Metadata["embeds"] = embeds;
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

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
                categories: request.Categories
            );
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostCreate,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        post.Publisher = publisher;

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var friendsResponse = await accounts.ListFriendsAsync(
            new ListRelationshipSimpleRequest { RelatedId = currentUser.Id }
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

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostReact,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                    { "reaction", Google.Protobuf.WellKnownTypes.Value.ForString(request.Symbol) },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(reaction);
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        if (request.Attitude == Shared.Models.PostReactionAttitude.Neutral)
            return BadRequest("You cannot create a neutral post award");

        var friendsResponse = await accounts.ListFriendsAsync(
            new ListRelationshipSimpleRequest { AccountId = currentUser.Id.ToString() }
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

        var orderRemark = string.IsNullOrWhiteSpace(post.Title)
            ? "from @" + post.Publisher.Name
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

    public class PostPinRequest
    {
        [Required] public Shared.Models.PostPinMode Mode { get; set; }
    }

    [HttpPost("{id:guid}/pin")]
    [Authorize]
    public async Task<ActionResult<SnPost>> PinPost(Guid id, [FromBody] PostPinRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
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

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostPin,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                    {
                        "mode",
                        Google.Protobuf.WellKnownTypes.Value.ForString(request.Mode.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(post);
    }

    [HttpDelete("{id:guid}/pin")]
    [Authorize]
    public async Task<ActionResult<SnPost>> UnpinPost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
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

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostUnpin,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
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
        if (string.IsNullOrWhiteSpace(request.Content) && request.Attachments is { Count: 0 })
            return BadRequest("Content is required.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) && request.Type != PostType.Article)
            return BadRequest("Thumbnail only supported in article.");
        if (!string.IsNullOrWhiteSpace(request.ThumbnailId) &&
            !(request.Attachments?.Contains(request.ThumbnailId) ?? false))
            return BadRequest("Thumbnail must be presented in attachment list.");

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
        if (!await pub.IsMemberWithRole(post.Publisher.Id, accountId, PublisherMemberRole.Editor))
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
        if (request.Type is not null)
            post.Type = request.Type.Value;
        if (request.Meta is not null)
            post.Metadata = request.Meta;

        // The same, this field can be null, so update it anyway.
        post.EmbedView = request.EmbedView;

        // All the fields are updated when the request contains the specific fields
        // But the Poll can be null, so it will be updated whatever it included in requests or not
        if (request.PollId.HasValue)
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
                publishedAt: request.PublishedAt
            );
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(err.Message);
        }

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostUpdate,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return Ok(post);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<SnPost>> DeletePost(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (
            !await pub.IsMemberWithRole(
                post.Publisher.Id,
                Guid.Parse(currentUser.Id),
                PublisherMemberRole.Editor
            )
        )
            return StatusCode(
                403,
                "You need at least be an editor to delete the publisher's post."
            );

        await ps.DeletePostAsync(post);

        _ = als.CreateActionLogAsync(
            new CreateActionLogRequest
            {
                Action = ActionLogType.PostDelete,
                Meta =
                {
                    {
                        "post_id",
                        Google.Protobuf.WellKnownTypes.Value.ForString(post.Id.ToString())
                    },
                },
                AccountId = currentUser.Id.ToString(),
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            }
        );

        return NoContent();
    }
}