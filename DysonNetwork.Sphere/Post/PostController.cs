using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;
using PublisherService = DysonNetwork.Sphere.Publisher.PublisherService;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
public class PostController(
    AppDatabase db,
    PostService ps,
    PostCollectionService pcs,
    PublisherService pub,
    RemoteAccountService remoteAccountsHelper,
    DyProfileService.DyProfileServiceClient accounts,
    IConfiguration configuration,
    DyEmbeddingService.DyEmbeddingServiceClient embeddings,
    RemoteRealmService rs,
    SponsorService sponsorService
) : ControllerBase
{
    private const string OrderDate = "date";
    private const string OrderPopularity = "popularity";

    public class UserReactionListingItem
    {
        public required SnPostReaction Reaction { get; set; }
        public required SnPost Post { get; set; }
    }

    private sealed record PostSearchContext(string Query, bool UseFuzzyMatch);

    private async Task<(HashSet<Guid>? gatekeptPublisherIds, HashSet<Guid>? subscriberPublisherIds, HashSet<Guid> closeFriendPublisherIds)> GetGatekeepInfoAsync(
        IQueryable<Guid> publisherIdsInQuery,
        DyAccount? currentUser)
    {
        var publisherIds = await publisherIdsInQuery.Distinct().ToListAsync();
        if (publisherIds.Count == 0)
            return (null, null, []);

        var gatekeptPublisherIds = (await db.Publishers
            .Where(p => publisherIds.Contains(p.Id) && p.GatekeptFollows == true)
            .Select(p => p.Id)
            .ToListAsync()).ToHashSet();

        HashSet<Guid>? subscriberPublisherIds = null;
        if (gatekeptPublisherIds.Count > 0)
        {
            if (currentUser != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var activeSubscriptions = await db.PublisherSubscriptions
                    .Where(s => s.AccountId == currentAccountId && s.EndedAt == null && publisherIds.Contains(s.PublisherId))
                    .Select(s => s.PublisherId)
                    .ToListAsync();
                subscriberPublisherIds = activeSubscriptions.ToHashSet();
            }
            else
            {
                subscriberPublisherIds = [];
            }
        }

        var closeFriendPublisherIds = await GetCloseFriendPublisherIdsAsync(publisherIds, currentUser);

        return (gatekeptPublisherIds.Count > 0 ? gatekeptPublisherIds : null, subscriberPublisherIds, closeFriendPublisherIds);
    }

    private async Task<HashSet<Guid>> GetCloseFriendPublisherIdsAsync(
        List<Guid> publisherIds,
        DyAccount? currentUser)
    {
        if (currentUser == null || publisherIds.Count == 0)
            return [];

        var currentAccountId = Guid.Parse(currentUser.Id);
        var closeFriendAccountIds = await remoteAccountsHelper.ListCloseFriendAccountIds(currentAccountId);
        if (closeFriendAccountIds.Count == 0)
            return [];

        var closeFriendSet = closeFriendAccountIds.ToHashSet();
        var publisherAccountIds = await db.Publishers
            .Where(p => publisherIds.Contains(p.Id) && p.AccountId != null)
            .Select(p => new { p.Id, AccountId = p.AccountId!.Value })
            .ToListAsync();

        return publisherAccountIds
            .Where(p => closeFriendSet.Contains(p.AccountId))
            .Select(p => p.Id)
            .ToHashSet();
    }

    public class ThreadedReplyNode
    {
        public required SnPost Post { get; set; }
        public required int Depth { get; set; }
        public required Guid? ParentId { get; set; }
    }

    private static void FlattenThreadedReplies(
        SnPost post,
        Dictionary<Guid, List<SnPost>> repliesByParent,
        int depth,
        List<ThreadedReplyNode> result
    )
    {
        post.RepliedPost = null;
        post.ForwardedPost = null;
        result.Add(new ThreadedReplyNode { Post = post, Depth = depth, ParentId = post.RepliedPostId });

        var replies = repliesByParent.GetValueOrDefault(post.Id, []);
        foreach (var reply in replies)
            FlattenThreadedReplies(reply, repliesByParent, depth + 1, result);
    }

    private static string? NormalizeSearchTerm(string? queryTerm)
    {
        var normalized = queryTerm?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static PostSearchContext? CreatePostSearchContext(string? queryTerm)
    {
        var normalized = NormalizeSearchTerm(queryTerm);
        return normalized is null ? null : new PostSearchContext(normalized, normalized.Length >= 3);
    }

    private static IQueryable<SnPost> ApplyPostTextSearch(IQueryable<SnPost> query, PostSearchContext? searchContext)
    {
        if (searchContext is null)
            return query;

        var searchPattern = $"%{searchContext.Query}%";
        if (!searchContext.UseFuzzyMatch)
        {
            return query.Where(p =>
                (p.Title != null && EF.Functions.ILike(p.Title, searchPattern))
                || (p.Description != null && EF.Functions.ILike(p.Description, searchPattern))
                || (p.Content != null && EF.Functions.ILike(p.Content, searchPattern))
            );
        }

        return query.Where(p =>
            (p.Title != null && (
                EF.Functions.ILike(p.Title, searchPattern)
                || EF.Functions.TrigramsAreSimilar(p.Title, searchContext.Query)
            ))
            || (p.Description != null && (
                EF.Functions.ILike(p.Description, searchPattern)
                || EF.Functions.TrigramsAreSimilar(p.Description, searchContext.Query)
            ))
            || (p.Content != null && (
                EF.Functions.ILike(p.Content, searchPattern)
                || EF.Functions.TrigramsAreWordSimilar(searchContext.Query, p.Content)
            ))
        );
    }

    private static IQueryable<SnPost> ApplyPostOrdering(
        IQueryable<SnPost> query,
        string? order,
        bool orderDesc,
        bool shuffle,
        PostSearchContext? searchContext
    )
    {
        if (shuffle)
            return query.OrderBy(e => EF.Functions.Random());

        var normalizedOrder = order?.Trim().ToLowerInvariant();
        var orderByDate = normalizedOrder == OrderDate;

        if (searchContext is { UseFuzzyMatch: true } && !orderByDate)
        {
            var searchPattern = $"%{searchContext.Query}%";
            IOrderedQueryable<SnPost> rankedQuery = query
                .OrderByDescending(p =>
                    (p.Title != null && EF.Functions.ILike(p.Title, searchPattern))
                    || (p.Description != null && EF.Functions.ILike(p.Description, searchPattern))
                    || (p.Content != null && EF.Functions.ILike(p.Content, searchPattern))
                )
                .ThenByDescending(p => p.Title != null ? EF.Functions.TrigramsSimilarity(p.Title, searchContext.Query) : 0.0f)
                .ThenByDescending(p => p.Description != null ? EF.Functions.TrigramsSimilarity(p.Description, searchContext.Query) : 0.0f)
                .ThenByDescending(p => p.Content != null ? EF.Functions.TrigramsWordSimilarity(searchContext.Query, p.Content) : 0.0f);

            return normalizedOrder switch
            {
                OrderPopularity => orderDesc
                    ? rankedQuery.ThenByDescending(e => e.Upvotes * 10 - e.Downvotes * 10 + e.AwardedScore)
                        .ThenByDescending(e => e.PublishedAt ?? e.CreatedAt)
                    : rankedQuery.ThenBy(e => e.Upvotes * 10 - e.Downvotes * 10 + e.AwardedScore)
                        .ThenByDescending(e => e.PublishedAt ?? e.CreatedAt),
                _ => orderDesc
                    ? rankedQuery.ThenByDescending(e => e.PublishedAt ?? e.CreatedAt)
                    : rankedQuery.ThenBy(e => e.PublishedAt ?? e.CreatedAt)
            };
        }

        return normalizedOrder switch
        {
            OrderPopularity => orderDesc
                ? query.OrderByDescending(e => e.Upvotes * 10 - e.Downvotes * 10 + e.AwardedScore)
                : query.OrderBy(e => e.Upvotes * 10 - e.Downvotes * 10 + e.AwardedScore),
            _ => orderDesc
                ? query.OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
                : query.OrderBy(e => e.PublishedAt ?? e.CreatedAt)
        };
    }

    [HttpGet("featured")]
    public async Task<ActionResult<List<SnPost>>> ListFeaturedPosts()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        var posts = await ps.ListFeaturedPostsAsync(currentUser);
        return Ok(posts);
    }

    [HttpGet("sponsor/current")]
    public async Task<ActionResult> GetCurrentSponsoredPost()
    {
        var post = await sponsorService.GetCurrentSponsoredPostAsync();
        if (post is null) return Ok(new { sponsored = false });
        return Ok(new { sponsored = true, post });
    }

    [HttpGet("sponsor/leaderboard")]
    public async Task<ActionResult> GetSponsorLeaderboard([FromQuery] int take = 20)
    {
        var entries = await sponsorService.GetLeaderboardAsync(take);
        return Ok(entries);
    }

    [HttpGet("{id:guid}/sponsor")]
    public async Task<ActionResult> GetPostSponsorship(Guid id)
    {
        var total = await sponsorService.GetPostTotalSponsorshipAsync(id);
        return Ok(new { total_amount = total });
    }

    [HttpGet("{id:guid}/sponsor/history")]
    [Authorize]
    public async Task<ActionResult> GetPostSponsorHistory(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var requesterId = Guid.Parse(currentUser.Id);
        var post = await db.Posts
            .Where(p => p.Id == id)
            .Select(p => new { p.PublisherId })
            .FirstOrDefaultAsync();
        if (post is null) return NotFound();

        Guid? authorAccountId = null;
        if (post.PublisherId.HasValue)
        {
            authorAccountId = await db.Publishers
                .Where(p => p.Id == post.PublisherId.Value && p.AccountId.HasValue)
                .Select(p => p.AccountId!.Value)
                .FirstOrDefaultAsync();
        }

        var history = await sponsorService.GetBidHistoryAsync(id, requesterId, authorAccountId);
        return Ok(history);
    }

    [HttpGet("drafts")]
    [Authorize]
    public async Task<ActionResult<List<SnPost>>> ListDrafts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "pub")] string? pubName = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var userPublishers = await pub.GetUserPublishers(accountId);
        var publisherIds = userPublishers.Select(p => p.Id).ToList();

        if (pubName is not null)
        {
            var selectedPublisher = await pub.GetPublisherByName(pubName);
            if (selectedPublisher is null)
                return NotFound();
            if (
                !await pub.IsMemberWithRole(
                    selectedPublisher.Id,
                    accountId,
                    PublisherMemberRole.Editor
                )
            )
                return StatusCode(403, "You need at least be an editor to view drafts.");

            publisherIds = [selectedPublisher.Id];
        }

        var query = db
            .Posts.Where(p =>
                p.DraftedAt != null && p.PublisherId.HasValue && publisherIds.Contains(p.PublisherId.Value)
            )
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .OrderByDescending(e => e.DraftedAt ?? e.UpdatedAt);

        var totalCount = await query.CountAsync();
        var posts = await query.Skip(offset).Take(take).ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(posts);
    }

    [HttpGet("bookmarks")]
    [Authorize]
    public async Task<ActionResult<List<SnPost>>> ListBookmarks(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "order")] string? order = null
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

        var bookmarkedPostQuery = db.PostBookmarks
            .Where(b => b.AccountId == accountId)
            .Select(b => b.Post)
            .Where(p => p.PublisherId != null)
            .Select(p => p.PublisherId!.Value);

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            bookmarkedPostQuery,
            currentUser
        );

        var visiblePostIds = await db.Posts
            .Where(p => db.PostBookmarks.Any(b => b.AccountId == accountId && b.PostId == p.Id))
            .FilterWithVisibility(
                currentUser,
                userFriends,
                userPublishers,
                isListing: true,
                gatekeptPublisherIds,
                subscriberPublisherIds,
                closeFriendPublisherIds: closeFriendPublisherIds
            )
            .Select(p => p.Id)
            .ToListAsync();

        var visibleBookmarks = db.PostBookmarks
            .Where(b => b.AccountId == accountId && visiblePostIds.Contains(b.PostId));

        var totalCount = await visibleBookmarks.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        visibleBookmarks = order?.ToLowerInvariant() switch
        {
            "created" => visibleBookmarks.OrderByDescending(b => b.CreatedAt),
            _ => visibleBookmarks.OrderByDescending(b => b.CreatedAt)
        };

        var posts = await visibleBookmarks
            .Include(b => b.Post)
            .ThenInclude(p => p.Publisher)
            .Include(b => b.Post)
            .ThenInclude(p => p.Categories)
            .Include(b => b.Post)
            .ThenInclude(p => p.Tags)
            .Include(b => b.Post)
            .ThenInclude(p => p.RepliedPost)
            .Include(b => b.Post)
            .ThenInclude(p => p.ForwardedPost)
            .Include(b => b.Post)
            .ThenInclude(p => p.FeaturedRecords)
            .Skip(offset)
            .Take(take)
            .Select(b => b.Post)
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync(posts);

        return Ok(posts);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SnPost>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [SwaggerOperation(
        Summary = "Retrieves a paginated list of posts",
        Description =
            "Gets posts with various filtering and sorting options. Supports pagination and advanced search capabilities.",
        OperationId = "ListPosts",
        Tags = ["Posts"]
    )]
    [SwaggerResponse(
        StatusCodes.Status200OK,
        "Successfully retrieved the list of posts",
        typeof(List<SnPost>)
    )]
    [SwaggerResponse(StatusCodes.Status400BadRequest, "Invalid request parameters")]
    public async Task<ActionResult<List<SnPost>>> ListPosts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "realm")] string? realmName = null,
        [FromQuery(Name = "type")] int? type = null,
        [FromQuery(Name = "categories")] List<string>? categories = null,
        [FromQuery(Name = "tags")] List<string>? tags = null,
        [FromQuery(Name = "query")] string? queryTerm = null,
        [FromQuery(Name = "media")] bool onlyMedia = false,
        [FromQuery(Name = "shuffle")] bool shuffle = false,
        [FromQuery(Name = "replies")] bool? includeReplies = null,
        [FromQuery(Name = "pinned")] bool? pinned = null,
        [FromQuery(Name = "order")] string? order = null,
        [FromQuery(Name = "orderDesc")] bool orderDesc = true,
        [FromQuery(Name = "periodStart")] int? periodStartTime = null,
        [FromQuery(Name = "periodEnd")] int? periodEndTime = null,
        [FromQuery(Name = "mentioned")] string? mentioned = null,
        [FromQuery(Name = "searchEngine")] string? searchEngine = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        Instant? periodStart = periodStartTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodStartTime.Value)
            : null;
        Instant? periodEnd = periodEndTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodEndTime.Value)
            : null;

        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);

        List<Guid> userFriends = [];
        HashSet<Guid> blockedAccountIds = [];
        List<Guid> mutedAccountIds = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
            blockedAccountIds = await remoteAccountsHelper.ListAllBlockedAccountIds(accountId);
            mutedAccountIds = await remoteAccountsHelper.ListMutedAccountIds(accountId);
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(accountId);
        var userRealms = currentUser is null ? [] : await rs.GetUserRealms(accountId);
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();
        var visibleRealmIds = userRealms.Concat(publicRealmIds).Distinct().ToList();

        var publisher =
            pubName == null
                ? null
                : await db.Publishers.FirstOrDefaultAsync(p => p.Name.ToLower() == pubName.ToLowerInvariant());
        var realm = realmName == null ? null : await rs.GetRealmBySlug(realmName);
        var defaultSearchEngine = configuration["Posts:SearchEngineDefault"] ?? "semantic";
        var searchContext = CreatePostSearchContext(queryTerm);
        var effectiveSearchEngine = string.IsNullOrWhiteSpace(searchEngine)
            ? defaultSearchEngine
            : searchEngine;
        var useSemanticSearch =
            searchContext is not null
            && !(
                string.Equals(effectiveSearchEngine, "fulltext", StringComparison.OrdinalIgnoreCase)
                || string.Equals(effectiveSearchEngine, "full-text", StringComparison.OrdinalIgnoreCase)
                || string.Equals(effectiveSearchEngine, "full_text", StringComparison.OrdinalIgnoreCase)
            );

        var query = db
            .Posts.Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .Where(p => p.FediverseUri == null)
            .AsQueryable();
        if (publisher != null)
            query = query.Where(p => p.PublisherId == publisher.Id);
        if (type != null)
            query = query.Where(p => p.Type == (Shared.Models.PostType)type);
        if (categories is { Count: > 0 })
            query = query.Where(p => p.Categories.Any(c => categories.Contains(c.Slug)));
        if (tags is { Count: > 0 })
            query = query.Where(p => p.Tags.Any(c => tags.Contains(c.Slug)));
        if (onlyMedia)
            query = query.Where(e => e.Attachments.Count > 0);

        if (realm != null)
            query = query.Where(p => p.RealmId == realm.Id);
        else if (string.IsNullOrWhiteSpace(pubName))
            query = query.Where(p =>
                p.RealmId == null || visibleRealmIds.Contains(p.RealmId.Value)
            );

        if (periodStart != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) >= periodStart);
        if (periodEnd != null)
            query = query.Where(p => (p.PublishedAt ?? p.CreatedAt) <= periodEnd);

        switch (pinned)
        {
            case true when realm != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.RealmPage);
                break;
            case true when publisher != null:
                query = query.Where(p => p.PinMode == Shared.Models.PostPinMode.PublisherPage);
                break;
            case true:
                return BadRequest(
                    "You need pass extra realm or publisher params in order to filter with pinned posts."
                );
            case false:
                query = query.Where(p => p.PinMode == null);
                break;
        }

        query = includeReplies switch
        {
            false => query.Where(e => e.RepliedPostId == null),
            true => query.Where(e => e.RepliedPostId != null),
            _ => query,
        };

        if (!string.IsNullOrWhiteSpace(mentioned))
        {
            var normalizedMentioned = mentioned.ToLowerInvariant();
            query = query.Where(p =>
                p.Content != null && (
                    EF.Functions.ILike(p.Content, $"%@{mentioned}%") ||
                    p.Mentions != null && p.Mentions.Any(m => m.Username != null && EF.Functions.ILike(m.Username, normalizedMentioned))
                )
            );
        }

        var publisherIdsInQuery = publisher != null
            ? new List<Guid> { publisher.Id }
            : await query.Where(p => p.PublisherId != null).Select(p => p.PublisherId!.Value).Distinct().ToListAsync();

        HashSet<Guid>? gatekeptPublisherIds = null;
        HashSet<Guid>? subscriberPublisherIds = null;
        HashSet<Guid>? shadowbannedPublisherIds = null;

        if (publisherIdsInQuery.Count > 0)
        {
            gatekeptPublisherIds = (await db.Publishers
                .Where(p => publisherIdsInQuery.Contains(p.Id) && p.GatekeptFollows == true)
                .Select(p => p.Id)
                .ToListAsync()).ToHashSet();

            shadowbannedPublisherIds = (await db.Publishers
                .Where(p => publisherIdsInQuery.Contains(p.Id) && p.ShadowbanReason != null && p.ShadowbanReason != PublisherShadowbanReason.None)
                .Select(p => p.Id)
                .ToListAsync()).ToHashSet();

            if (gatekeptPublisherIds.Count > 0)
            {
                if (currentUser != null)
                {
                    var currentAccountId = Guid.Parse(currentUser.Id);
                    var activeSubscriptions = await db.PublisherSubscriptions
                        .Where(s => s.AccountId == currentAccountId && s.EndedAt == null && publisherIdsInQuery.Contains(s.PublisherId))
                        .Select(s => s.PublisherId)
                        .ToListAsync();
                    subscriberPublisherIds = activeSubscriptions.ToHashSet();
                }
                else
                {
                    subscriberPublisherIds = [];
                }
            }
        }

        var closeFriendPublisherIds = await GetCloseFriendPublisherIdsAsync(publisherIdsInQuery, currentUser);

        query = query.FilterWithVisibility(
            currentUser,
            userFriends,
            userPublishers,
            isListing: true,
            gatekeptPublisherIds,
            subscriberPublisherIds,
            blockedAccountIds,
            mutedAccountIds.ToHashSet(),
            closeFriendPublisherIds,
            showQuietPublic: pubName is not null
        );

        if (shadowbannedPublisherIds != null && shadowbannedPublisherIds.Count > 0)
        {
            query = query.Where(p =>
                !shadowbannedPublisherIds.Contains(p.PublisherId!.Value) &&
                (p.ShadowbanReason == null || p.ShadowbanReason == PostShadowbanReason.None));
        }

        if (useSemanticSearch)
        {
            try
            {
                var embeddingResponse = await embeddings.GenerateEmbeddingAsync(
                    new DyGenerateEmbeddingRequest { Text = searchContext!.Query }
                );
                var queryEmbedding = new Vector(embeddingResponse.Embedding.ToArray());

                var semanticQuery = query.Join(
                    db.PostIndices.Where(i => i.Embedding != null),
                    post => post.Id,
                    index => index.PostId,
                    (post, index) => new
                    {
                        post.Id,
                        Distance = index.Embedding!.CosineDistance(queryEmbedding),
                    }
                );

                var semanticTotalCount = await semanticQuery.CountAsync();
                var rankedPostIds = await semanticQuery
                    .OrderBy(x => x.Distance)
                    .Skip(offset)
                    .Take(take)
                    .Select(x => x.Id)
                    .ToListAsync();

                var rankedPosts = await query.Where(p => rankedPostIds.Contains(p.Id)).ToListAsync();
                var rankedPostsMap = rankedPosts.ToDictionary(p => p.Id);
                var semanticPosts = rankedPostIds
                    .Where(rankedPostsMap.ContainsKey)
                    .Select(id => rankedPostsMap[id])
                    .ToList();

                foreach (var post in semanticPosts)
                {
                    if (post.RepliedPost != null)
                        post.RepliedPost.RepliedPost = null;
                }

                semanticPosts = await ps.LoadPostInfo(semanticPosts, currentUser, true);
                await pcs.LoadPublisherCollectionsAsync(semanticPosts);
                await LoadPostsRealmsAsync(semanticPosts, rs);

                Response.Headers["X-Total"] = semanticTotalCount.ToString();
                return Ok(semanticPosts);
            }
            catch (Grpc.Core.RpcException)
            {
                return StatusCode(503, "Semantic search is currently unavailable.");
            }
        }

        query = ApplyPostTextSearch(query, searchContext);
        query = ApplyPostOrdering(query, order, orderDesc, shuffle, searchContext);

        var totalCount = await query.CountAsync();

        if (pubName is not null && totalCount == 0 && publisher is not null)
        {
            var publisherAccountId = publisher.AccountId;
            
            if (publisher.IsGatekept)
            {
                var isSubscriber = currentUser is not null
                    && subscriberPublisherIds is not null
                    && subscriberPublisherIds.Contains(publisher.Id);
                
                if (!isSubscriber)
                {
                    string? publisherOwnerName = null;
                    if (publisherAccountId.HasValue)
                    {
                        try
                        {
                            var account = await accounts.GetAccountAsync(new DyGetAccountRequest { Id = publisherAccountId.Value.ToString() });
                            publisherOwnerName = account.Nick ?? account.Name;
                        }
                        catch
                        {
                            // Ignore if account not found
                        }
                    }
                    
                    return StatusCode(403, new ApiError
                    {
                        Code = "PUBLISHER_GATEKEPT",
                        Message = $"{publisher.Name}'s posts are only available to subscribers.",
                        Status = 403,
                        Detail = publisherOwnerName is not null
                            ? $"Subscribe to {publisherOwnerName}'s publisher to access their posts."
                            : "Subscribe to this publisher to access their posts.",
                        Meta = new Dictionary<string, object?>
                        {
                            ["publisher"] = publisher.Name,
                            ["is_gatekept"] = true,
                            ["requires_subscription"] = true
                        }
                    });
                }
            }
            
            if (publisherAccountId.HasValue && currentUser is not null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var isBlocked = await remoteAccountsHelper.IsBlockedEitherDirection(currentAccountId, publisherAccountId.Value);
                
                if (isBlocked)
                {
                    var isBlockedByPublisher = blockedAccountIds.Contains(publisherAccountId.Value);
                    
                    return StatusCode(403, new ApiError
                    {
                        Code = isBlockedByPublisher ? "BLOCKED_BY_PUBLISHER" : "PUBLISHER_BLOCKED",
                        Message = isBlockedByPublisher
                            ? "You cannot view this publisher's posts because they have blocked you."
                            : "You have blocked this publisher. Unblock them to view their posts.",
                        Status = 403,
                        Meta = new Dictionary<string, object?>
                        {
                            ["publisher"] = publisher.Name,
                            ["is_blocked"] = true,
                            ["blocked_by_publisher"] = isBlockedByPublisher
                        }
                    });
                }
            }
            
            if (currentUser is null && publisher.IsGatekept)
            {
                return StatusCode(401, new ApiError
                {
                    Code = "AUTHENTICATION_REQUIRED",
                    Message = "Authentication is required to view this publisher's posts.",
                    Status = 401,
                    Detail = "This publisher requires subscribers to be authenticated.",
                    Meta = new Dictionary<string, object?>
                    {
                        ["publisher"] = publisher.Name,
                        ["is_gatekept"] = true,
                        ["requires_authentication"] = true
                    }
                });
            }
        }

        var posts = await query.Skip(offset).Take(take).ToListAsync();
        foreach (var post in posts)
        {
            if (post.RepliedPost != null)
                post.RepliedPost.RepliedPost = null;
        }
        

        posts = await ps.LoadPostInfo(posts, currentUser, true);

        await pcs.LoadPublisherCollectionsAsync(posts);
        await LoadPostsRealmsAsync(posts, rs);

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    private static async Task LoadPostsRealmsAsync(List<SnPost> posts, RemoteRealmService rs)
    {
        var postRealmIds = posts
            .Where(p => p.RealmId != null)
            .Select(p => p.RealmId!.Value)
            .Distinct()
            .ToList();
        if (!postRealmIds.Any())
            return;

        var realms = await rs.GetRealmBatch(postRealmIds.Select(id => id.ToString()).ToList());
        var realmDict = realms.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.FirstOrDefault());

        foreach (var post in posts.Where(p => p.RealmId != null))
        {
            if (realmDict.TryGetValue(post.RealmId!.Value, out var realm))
            {
                post.Realm = realm;
            }
        }
    }

    [HttpGet("{publisherName}/{slug}")]
    public async Task<ActionResult<SnPost>> GetPost(string publisherName, string slug)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var lowerSlug = slug?.ToLowerInvariant() ?? string.Empty;
        var lowerPublisherName = publisherName?.ToLowerInvariant() ?? string.Empty;
        var post = await db.Posts
            .Include(e => e.Publisher)
            .Where(e => e.Slug.ToLower() == lowerSlug && e.Publisher != null && e.Publisher.Name.ToLower() == lowerPublisherName)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.PublisherId.HasValue && (post.Publisher?.GatekeptFollows == true || post.Visibility == Shared.Models.PostVisibility.QuietPublic))
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == post.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == post.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        if (post.Visibility == Shared.Models.PostVisibility.CloseFriendsOnly)
        {
            if (currentUser == null)
                return StatusCode(403, "Close friends access required");
            if (post.Publisher?.AccountId != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var isCloseFriend = await remoteAccountsHelper.IsCloseFriend(post.Publisher.AccountId.Value, currentAccountId);
                if (!isCloseFriend && !userPublishers.Any(p => p.Id == post.PublisherId!.Value))
                    return StatusCode(403, "Close friends access required");
            }
        }

        post = await ps.LoadPostInfo(post, currentUser);
        await pcs.LoadPublisherCollectionsAsync([post]);
        if (post.RealmId != null)
        {
            post.Realm = await rs.GetRealm(post.RealmId.Value.ToString());
        }

        if (currentUser != null)
            await ps.IncreaseViewCount(post.Id, currentUser.Id, isDetailView: true);
        else
            await ps.IncreaseViewCount(post.Id, isDetailView: true);

        return Ok(post);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnPost>> GetPost(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db
            .Posts.Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();

        if (post.PublisherId.HasValue && (post.Publisher?.GatekeptFollows == true || post.Visibility == Shared.Models.PostVisibility.QuietPublic))
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == post.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == post.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        if (post.Visibility == Shared.Models.PostVisibility.CloseFriendsOnly)
        {
            if (currentUser == null)
                return StatusCode(403, "Close friends access required");
            if (post.Publisher?.AccountId != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var isCloseFriend = await remoteAccountsHelper.IsCloseFriend(post.Publisher.AccountId.Value, currentAccountId);
                if (!isCloseFriend && !userPublishers.Any(p => p.Id == post.PublisherId!.Value))
                    return StatusCode(403, "Close friends access required");
            }
        }

        post = await ps.LoadPostInfo(post, currentUser);
        await pcs.LoadPublisherCollectionsAsync([post]);
        if (post.RealmId != null)
            post.Realm = await rs.GetRealm(post.RealmId.Value.ToString());

        if (currentUser != null)
            await ps.IncreaseViewCount(post.Id, currentUser.Id, isDetailView: true);
        else
            await ps.IncreaseViewCount(post.Id, isDetailView: true);

        return Ok(post);
    }

    [HttpGet("{id:guid}/prev")]
    public async Task<ActionResult<SnPost>> GetPrevPost(
        Guid id,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "realm")] string? realmName = null,
        [FromQuery(Name = "type")] int? type = null,
        [FromQuery(Name = "categories")] List<string>? categories = null,
        [FromQuery(Name = "tags")] List<string>? tags = null,
        [FromQuery(Name = "query")] string? queryTerm = null,
        [FromQuery(Name = "media")] bool onlyMedia = false,
        [FromQuery(Name = "replies")] bool? includeReplies = null,
        [FromQuery(Name = "pinned")] bool? pinned = null,
        [FromQuery(Name = "periodStart")] int? periodStartTime = null,
        [FromQuery(Name = "periodEnd")] int? periodEndTime = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(accountId);
        var userRealms = currentUser is null ? [] : await rs.GetUserRealms(accountId);
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();
        var visibleRealmIds = userRealms.Concat(publicRealmIds).Distinct().ToList();

        var publisher = pubName == null
            ? null
            : await db.Publishers.FirstOrDefaultAsync(p => p.Name.ToLower() == pubName.ToLowerInvariant());
        var realm = realmName == null ? null : await rs.GetRealmBySlug(realmName);

        Instant? periodStart = periodStartTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodStartTime.Value)
            : null;
        Instant? periodEnd = periodEndTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodEndTime.Value)
            : null;

        var currentPost = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (currentPost is null)
            return NotFound("Current post not found");

        var currentTime = currentPost.PublishedAt ?? currentPost.CreatedAt;

        var baseQuery = db.Posts
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .Where(p => p.FediverseUri == null);

        if (publisher != null)
            baseQuery = baseQuery.Where(p => p.PublisherId == publisher.Id);
        if (type != null)
            baseQuery = baseQuery.Where(p => p.Type == (Shared.Models.PostType)type);
        if (categories is { Count: > 0 })
            baseQuery = baseQuery.Where(p => p.Categories.Any(c => categories.Contains(c.Slug)));
        if (tags is { Count: > 0 })
            baseQuery = baseQuery.Where(p => p.Tags.Any(c => tags.Contains(c.Slug)));
        if (onlyMedia)
            baseQuery = baseQuery.Where(e => e.Attachments.Count > 0);

        if (realm != null)
            baseQuery = baseQuery.Where(p => p.RealmId == realm.Id);
        else if (string.IsNullOrWhiteSpace(pubName))
            baseQuery = baseQuery.Where(p => p.RealmId == null || visibleRealmIds.Contains(p.RealmId.Value));

        if (periodStart != null)
            baseQuery = baseQuery.Where(p => (p.PublishedAt ?? p.CreatedAt) >= periodStart);
        if (periodEnd != null)
            baseQuery = baseQuery.Where(p => (p.PublishedAt ?? p.CreatedAt) <= periodEnd);

        switch (pinned)
        {
            case true when realm != null:
                baseQuery = baseQuery.Where(p => p.PinMode == Shared.Models.PostPinMode.RealmPage);
                break;
            case true when publisher != null:
                baseQuery = baseQuery.Where(p => p.PinMode == Shared.Models.PostPinMode.PublisherPage);
                break;
            case true:
                return BadRequest("You need pass extra realm or publisher params in order to filter with pinned posts.");
            case false:
                baseQuery = baseQuery.Where(p => p.PinMode == null);
                break;
        }

        baseQuery = includeReplies switch
        {
            false => baseQuery.Where(e => e.RepliedPostId == null),
            true => baseQuery.Where(e => e.RepliedPostId != null),
            _ => baseQuery,
        };

        baseQuery = ApplyPostTextSearch(baseQuery, CreatePostSearchContext(queryTerm));

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            baseQuery.Where(p => p.PublisherId != null).Select(p => p.PublisherId!.Value),
            currentUser
        );

        var query = baseQuery
            .Where(e => (e.PublishedAt ?? e.CreatedAt) < currentTime)
            .Include(e => e.Publisher)
            .FilterWithVisibility(
                currentUser,
                userFriends,
                userPublishers,
                isListing: true,
                gatekeptPublisherIds,
                subscriberPublisherIds
            );

        var prevPost = await query
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .FirstOrDefaultAsync();

        if (prevPost is null)
            return NotFound("No previous post found");

        if (prevPost.PublisherId.HasValue && (prevPost.Publisher?.GatekeptFollows == true || prevPost.Visibility == Shared.Models.PostVisibility.QuietPublic))
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == prevPost.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == prevPost.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        if (prevPost.Visibility == Shared.Models.PostVisibility.CloseFriendsOnly)
        {
            if (currentUser == null)
                return StatusCode(403, "Close friends access required");
            if (prevPost.Publisher?.AccountId != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var isCloseFriend = await remoteAccountsHelper.IsCloseFriend(prevPost.Publisher.AccountId.Value, currentAccountId);
                if (!isCloseFriend && !userPublishers.Any(p => p.Id == prevPost.PublisherId!.Value))
                    return StatusCode(403, "Close friends access required");
            }
        }

        prevPost = await ps.LoadPostInfo(prevPost, currentUser);
        await pcs.LoadPublisherCollectionsAsync([prevPost]);
        if (prevPost.RealmId != null)
            prevPost.Realm = await rs.GetRealm(prevPost.RealmId.Value.ToString());

        return Ok(prevPost);
    }

    [HttpGet("{id:guid}/next")]
    public async Task<ActionResult<SnPost>> GetNextPost(
        Guid id,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "realm")] string? realmName = null,
        [FromQuery(Name = "type")] int? type = null,
        [FromQuery(Name = "categories")] List<string>? categories = null,
        [FromQuery(Name = "tags")] List<string>? tags = null,
        [FromQuery(Name = "query")] string? queryTerm = null,
        [FromQuery(Name = "media")] bool onlyMedia = false,
        [FromQuery(Name = "replies")] bool? includeReplies = null,
        [FromQuery(Name = "pinned")] bool? pinned = null,
        [FromQuery(Name = "periodStart")] int? periodStartTime = null,
        [FromQuery(Name = "periodEnd")] int? periodEndTime = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(accountId);
        var userRealms = currentUser is null ? [] : await rs.GetUserRealms(accountId);
        var publicRealms = await rs.GetPublicRealms();
        var publicRealmIds = publicRealms.Select(r => r.Id).ToList();
        var visibleRealmIds = userRealms.Concat(publicRealmIds).Distinct().ToList();

        var publisher = pubName == null
            ? null
            : await db.Publishers.FirstOrDefaultAsync(p => p.Name.ToLower() == pubName.ToLowerInvariant());
        var realm = realmName == null ? null : await rs.GetRealmBySlug(realmName);

        Instant? periodStart = periodStartTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodStartTime.Value)
            : null;
        Instant? periodEnd = periodEndTime.HasValue
            ? Instant.FromUnixTimeSeconds(periodEndTime.Value)
            : null;

        var currentPost = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (currentPost is null)
            return NotFound("Current post not found");

        var currentTime = currentPost.PublishedAt ?? currentPost.CreatedAt;

        var baseQuery = db.Posts
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .Where(p => p.FediverseUri == null);

        if (publisher != null)
            baseQuery = baseQuery.Where(p => p.PublisherId == publisher.Id);
        if (type != null)
            baseQuery = baseQuery.Where(p => p.Type == (Shared.Models.PostType)type);
        if (categories is { Count: > 0 })
            baseQuery = baseQuery.Where(p => p.Categories.Any(c => categories.Contains(c.Slug)));
        if (tags is { Count: > 0 })
            baseQuery = baseQuery.Where(p => p.Tags.Any(c => tags.Contains(c.Slug)));
        if (onlyMedia)
            baseQuery = baseQuery.Where(e => e.Attachments.Count > 0);

        if (realm != null)
            baseQuery = baseQuery.Where(p => p.RealmId == realm.Id);
        else if (string.IsNullOrWhiteSpace(pubName))
            baseQuery = baseQuery.Where(p => p.RealmId == null || visibleRealmIds.Contains(p.RealmId.Value));

        if (periodStart != null)
            baseQuery = baseQuery.Where(p => (p.PublishedAt ?? p.CreatedAt) >= periodStart);
        if (periodEnd != null)
            baseQuery = baseQuery.Where(p => (p.PublishedAt ?? p.CreatedAt) <= periodEnd);

        switch (pinned)
        {
            case true when realm != null:
                baseQuery = baseQuery.Where(p => p.PinMode == Shared.Models.PostPinMode.RealmPage);
                break;
            case true when publisher != null:
                baseQuery = baseQuery.Where(p => p.PinMode == Shared.Models.PostPinMode.PublisherPage);
                break;
            case true:
                return BadRequest("You need pass extra realm or publisher params in order to filter with pinned posts.");
            case false:
                baseQuery = baseQuery.Where(p => p.PinMode == null);
                break;
        }

        baseQuery = includeReplies switch
        {
            false => baseQuery.Where(e => e.RepliedPostId == null),
            true => baseQuery.Where(e => e.RepliedPostId != null),
            _ => baseQuery,
        };

        baseQuery = ApplyPostTextSearch(baseQuery, CreatePostSearchContext(queryTerm));

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            baseQuery.Where(p => p.PublisherId != null).Select(p => p.PublisherId!.Value),
            currentUser
        );

        var query = baseQuery
            .Where(e => (e.PublishedAt ?? e.CreatedAt) > currentTime)
            .Include(e => e.Publisher)
            .FilterWithVisibility(
                currentUser,
                userFriends,
                userPublishers,
                isListing: true,
                gatekeptPublisherIds,
                subscriberPublisherIds
            );

        var nextPost = await query
            .OrderBy(e => e.PublishedAt ?? e.CreatedAt)
            .FirstOrDefaultAsync();

        if (nextPost is null)
            return NotFound("No next post found");

        if (nextPost.PublisherId.HasValue && (nextPost.Publisher?.GatekeptFollows == true || nextPost.Visibility == Shared.Models.PostVisibility.QuietPublic))
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == nextPost.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == nextPost.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        if (nextPost.Visibility == Shared.Models.PostVisibility.CloseFriendsOnly)
        {
            if (currentUser == null)
                return StatusCode(403, "Close friends access required");
            if (nextPost.Publisher?.AccountId != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var isCloseFriend = await remoteAccountsHelper.IsCloseFriend(nextPost.Publisher.AccountId.Value, currentAccountId);
                if (!isCloseFriend && !userPublishers.Any(p => p.Id == nextPost.PublisherId!.Value))
                    return StatusCode(403, "Close friends access required");
            }
        }

        nextPost = await ps.LoadPostInfo(nextPost, currentUser);
        await pcs.LoadPublisherCollectionsAsync([nextPost]);
        if (nextPost.RealmId != null)
            nextPost.Realm = await rs.GetRealm(nextPost.RealmId.Value.ToString());

        return Ok(nextPost);
    }

    [HttpGet("{id:guid}/reactions")]
    public async Task<ActionResult<List<SnPostReaction>>> GetReactions(
        Guid id,
        [FromQuery] string? symbol = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "order")] string? order = null
    )
    {
        var query = db.PostReactions.Where(e => e.PostId == id);
        if (symbol is not null)
            query = query.Where(e => e.Symbol == symbol);

        var totalCount = await query.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        query = order?.ToLowerInvariant() switch
        {
            "created" => query.OrderByDescending(r => r.CreatedAt),
            _ => query.OrderBy(r => r.Symbol).ThenByDescending(r => r.CreatedAt)
        };

        var reactions = await query
            .Include(r => r.Actor)
            .ThenInclude(r => r!.Instance)
            .Take(take)
            .Skip(offset)
            .ToListAsync();

        var accountsProto = await remoteAccountsHelper.GetAccountBatch(
            reactions.Where(r => r.AccountId.HasValue).Select(r => r.AccountId!.Value).ToList()
        );
        var accountsData = accountsProto.ToDictionary(
            a => Guid.Parse(a.Id),
            SnAccount.FromProtoValue
        );

        foreach (var reaction in reactions)
            if (reaction.AccountId.HasValue && accountsData.TryGetValue(reaction.AccountId.Value, out var account))
                reaction.Account = account;

        return Ok(reactions);
    }

    [HttpGet("reactions/users/{name}")]
    public async Task<ActionResult<List<UserReactionListingItem>>> ListUserReactions(
        string name,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "order")] string? order = null
    )
    {
        var account = (await remoteAccountsHelper.SearchAccounts(name))
            .Select(SnAccount.FromProtoValue)
            .FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (account is null)
            return NotFound();

        var accountId = account.Id;

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var localPostQuery = db.PostReactions
            .Where(r => r.AccountId == accountId && r.Post.FediverseUri == null)
            .Select(r => r.Post)
            .Where(p => p.PublisherId != null);

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            localPostQuery.Select(p => p.PublisherId!.Value),
            currentUser
        );

        var visiblePostIds = await db.Posts
            .Where(p => db.PostReactions.Any(r => r.AccountId == accountId && r.PostId == p.Id && r.Post.FediverseUri == null))
            .FilterWithVisibility(
                currentUser,
                userFriends,
                userPublishers,
                isListing: true,
                gatekeptPublisherIds,
                subscriberPublisherIds
            )
            .Select(p => p.Id)
            .ToListAsync();

        var visibleReactions = db.PostReactions
            .Where(r => r.AccountId == accountId && r.Post.FediverseUri == null && visiblePostIds.Contains(r.PostId));

        var totalCount = await visibleReactions.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        visibleReactions = order?.ToLowerInvariant() switch
        {
            "created" => visibleReactions.OrderByDescending(r => r.CreatedAt),
            _ => visibleReactions.OrderByDescending(r => r.CreatedAt)
        };

        var reactions = await visibleReactions
            .Include(r => r.Actor)
            .Include(r => r.Post)
            .ThenInclude(p => p.Publisher)
            .Include(r => r.Post)
            .ThenInclude(p => p.Categories)
            .Include(r => r.Post)
            .ThenInclude(p => p.Tags)
            .Include(r => r.Post)
            .ThenInclude(p => p.RepliedPost)
            .Include(r => r.Post)
            .ThenInclude(p => p.ForwardedPost)
            .Include(r => r.Post)
            .ThenInclude(p => p.FeaturedRecords)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        foreach (var reaction in reactions)
        {
            if (reaction.AccountId == accountId)
                reaction.Account = account;
        }

        var posts = reactions.Select(r => r.Post).ToList();
        posts = await ps.LoadPostInfo(posts, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync(posts);

        var postsById = posts.ToDictionary(p => p.Id);
        var result = reactions
            .Where(r => postsById.ContainsKey(r.PostId))
            .Select(r => new UserReactionListingItem
            {
                Reaction = r,
                Post = postsById[r.PostId]
            })
            .ToList();

        return Ok(result);
    }

    [HttpGet("{id:guid}/replies/featured")]
    public async Task<ActionResult<SnPost>> GetFeaturedReply(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var now = SystemClock.Instance.GetCurrentInstant();
        var post = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .OrderByDescending(p =>
                p.Upvotes * 2 - p.Downvotes + ((p.CreatedAt - now).TotalMinutes < 60 ? 5 : 0)
            )
            .FilterWithVisibility(currentUser, userFriends, userPublishers, gatekeptPublisherIds: gatekeptPublisherIds, followerPublisherIds: subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .FirstOrDefaultAsync();
        if (post is null)
            return NotFound();
        post = await ps.LoadPostInfo(post, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync([post]);

        return Ok(post);
    }

    [HttpGet("{id:guid}/replies/pinned")]
    public async Task<ActionResult<List<SnPost>>> ListPinnedReplies(Guid id)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var posts = await db.Posts
            .Where(e =>
                e.RepliedPostId == id && e.PinMode == Shared.Models.PostPinMode.ReplyPage
            )
            .OrderByDescending(p => p.CreatedAt)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, gatekeptPublisherIds: gatekeptPublisherIds, followerPublisherIds: subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser);
        await pcs.LoadPublisherCollectionsAsync(posts);

        return Ok(posts);
    }

    [HttpGet("{id:guid}/replies")]
    public async Task<ActionResult<List<SnPost>>> ListReplies(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var parent = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (parent is null)
            return NotFound();

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var totalCount = await db
            .Posts.Where(e => e.RepliedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .CountAsync();
        var posts = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        posts = await ps.LoadPostInfo(posts, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync(posts);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount = reactionMaps.TryGetValue(post.Id, out var count)
                ? count
                : new Dictionary<string, int>();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }

    [HttpGet("{id:guid}/replies/threaded")]
    public async Task<ActionResult<List<ThreadedReplyNode>>> ListThreadedReplies(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var parent = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (parent is null)
            return NotFound();

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var totalCount = await db
            .Posts.Where(e => e.RepliedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .CountAsync();

        var rootReplies = await db
            .Posts.Where(e => e.RepliedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .AsNoTracking()
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        rootReplies = await ps.LoadPostInfo(rootReplies, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync(rootReplies);

        Response.Headers["X-Total"] = totalCount.ToString();

        if (rootReplies.Count == 0)
            return Ok(new List<ThreadedReplyNode>());

        var repliesByParent = new Dictionary<Guid, List<SnPost>>();
        var visited = rootReplies.Select(e => e.Id).ToHashSet();
        var frontier = rootReplies.Select(e => e.Id).ToList();

        while (frontier.Count > 0)
        {
            var children = await db
                .Posts.Where(e => e.RepliedPostId != null && frontier.Contains(e.RepliedPostId.Value))
                .Include(e => e.ForwardedPost)
                .Include(e => e.Categories)
                .Include(e => e.Tags)
                .Include(e => e.FeaturedRecords)
                .AsNoTracking()
                .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
                .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
                .ToListAsync();

            children = children.Where(e => visited.Add(e.Id)).ToList();
            if (children.Count == 0)
                break;

            children = await ps.LoadPostInfo(children, currentUser, true);
            await pcs.LoadPublisherCollectionsAsync(children);

            foreach (var child in children)
            {
                if (child.RepliedPostId is not { } parentId)
                    continue;

                if (!repliesByParent.TryGetValue(parentId, out var siblings))
                {
                    siblings = [];
                    repliesByParent[parentId] = siblings;
                }

                siblings.Add(child);
            }

            frontier = children.Select(e => e.Id).ToList();
        }

        var tree = new List<ThreadedReplyNode>();
        foreach (var root in rootReplies)
            FlattenThreadedReplies(root, repliesByParent, 0, tree);
        return Ok(tree);
    }

    public class PostThreadResponse
    {
        public List<ThreadedReplyNode>? Ancestors { get; set; }
        public required ThreadedReplyNode Current { get; set; }
        public required List<ThreadedReplyNode> Descendants { get; set; }
        public required bool HasMore { get; set; }
    }

    [HttpGet("{id:guid}/thread")]
    public async Task<ActionResult<PostThreadResponse>> GetThread(
        Guid id,
        [FromQuery] bool ancestors = true,
        [FromQuery] int ancestorLimit = 50,
        [FromQuery] int take = 20
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var currentPost = await db.Posts
            .Where(e => e.Id == id)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .Include(e => e.RepliedPost)
            .Include(e => e.ForwardedPost)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        if (currentPost is null)
            return NotFound();

        if (currentPost.PublisherId.HasValue && (currentPost.Publisher?.GatekeptFollows == true || currentPost.Visibility == Shared.Models.PostVisibility.QuietPublic))
        {
            if (currentUser == null)
                return StatusCode(403, "Subscriber access required");
            var currentAccountId = Guid.Parse(currentUser.Id);
            var isSubscriber = await db.PublisherSubscriptions
                .AnyAsync(s => s.PublisherId == currentPost.PublisherId.Value && s.AccountId == currentAccountId && s.EndedAt == null);
            if (!isSubscriber && !userPublishers.Any(p => p.Id == currentPost.PublisherId.Value))
                return StatusCode(403, "Subscriber access required");
        }

        if (currentPost.Visibility == Shared.Models.PostVisibility.CloseFriendsOnly)
        {
            if (currentUser == null)
                return StatusCode(403, "Close friends access required");
            if (currentPost.Publisher?.AccountId != null)
            {
                var currentAccountId = Guid.Parse(currentUser.Id);
                var isCloseFriend = await remoteAccountsHelper.IsCloseFriend(currentPost.Publisher.AccountId.Value, currentAccountId);
                if (!isCloseFriend && !userPublishers.Any(p => p.Id == currentPost.PublisherId!.Value))
                    return StatusCode(403, "Close friends access required");
            }
        }

        currentPost = await ps.LoadPostInfo(currentPost, currentUser);
        await pcs.LoadPublisherCollectionsAsync([currentPost]);
        if (currentPost.RealmId != null)
            currentPost.Realm = await rs.GetRealm(currentPost.RealmId.Value.ToString());

        List<ThreadedReplyNode>? ancestorNodes = null;
        if (ancestors)
        {
            var ancestorIdsRaw = await db.Database.SqlQueryRaw<Guid>(
                """
                WITH RECURSIVE ancestor_chain AS (
                    SELECT id, replied_post_id, 0 AS depth
                    FROM posts
                    WHERE id = {0} AND replied_post_id IS NOT NULL
                    UNION ALL
                    SELECT p.id, p.replied_post_id, ac.depth + 1
                    FROM posts p
                    INNER JOIN ancestor_chain ac ON p.id = ac.replied_post_id
                    WHERE p.deleted_at IS NULL
                )
                SELECT id FROM ancestor_chain WHERE depth > 0 ORDER BY depth DESC LIMIT {1}
                """,
                id, ancestorLimit
            ).ToListAsync();

            if (ancestorIdsRaw.Count > 0)
            {
                var ancestorPosts = await db.Posts
                    .Where(e => ancestorIdsRaw.Contains(e.Id))
                    .Include(e => e.Publisher)
                    .Include(e => e.Tags)
                    .Include(e => e.Categories)
                    .Include(e => e.ForwardedPost)
                    .Include(e => e.FeaturedRecords)
                    .FilterWithVisibility(currentUser, userFriends, userPublishers)
                    .ToListAsync();

                ancestorPosts = await ps.LoadPostInfo(ancestorPosts, currentUser);
                await pcs.LoadPublisherCollectionsAsync(ancestorPosts);

                var ancestorRealmIds = ancestorPosts
                    .Where(p => p.RealmId != null)
                    .Select(p => p.RealmId!.Value)
                    .Distinct()
                    .ToList();
                if (ancestorRealmIds.Count > 0)
                {
                    var realms = await rs.GetRealmBatch(ancestorRealmIds.Select(rid => rid.ToString()).ToList());
                    var realmDict = realms.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.FirstOrDefault());
                    foreach (var post in ancestorPosts.Where(p => p.RealmId != null))
                        if (realmDict.TryGetValue(post.RealmId!.Value, out var realm))
                            post.Realm = realm;
                }

                var ancestorOrder = ancestorIdsRaw.Select((aid, idx) => new { aid, idx }).ToDictionary(x => x.aid, x => x.idx);
                ancestorPosts.Sort((a, b) =>
                    ancestorOrder.GetValueOrDefault(a.Id, int.MaxValue)
                        .CompareTo(ancestorOrder.GetValueOrDefault(b.Id, int.MaxValue)));

                ancestorNodes = [];
                for (var i = 0; i < ancestorPosts.Count; i++)
                {
                    ancestorNodes.Add(new ThreadedReplyNode
                    {
                        Post = ancestorPosts[i],
                        Depth = i,
                        ParentId = ancestorPosts[i].RepliedPostId
                    });
                }
            }
        }

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.RepliedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var rootReplies = await db.Posts
            .Where(e => e.RepliedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .AsNoTracking()
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .ToListAsync();

        rootReplies = await ps.LoadPostInfo(rootReplies, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync(rootReplies);

        var descendants = new List<ThreadedReplyNode>();
        var hasMore = false;
        if (rootReplies.Count > 0)
        {
            var repliesByParent = new Dictionary<Guid, List<SnPost>>();
            var visited = rootReplies.Select(e => e.Id).ToHashSet();
            var frontier = rootReplies.Select(e => e.Id).ToList();

            while (frontier.Count > 0)
            {
                var children = await db.Posts
                    .Where(e => e.RepliedPostId != null && frontier.Contains(e.RepliedPostId.Value))
                    .Include(e => e.ForwardedPost)
                    .Include(e => e.Categories)
                    .Include(e => e.Tags)
                    .Include(e => e.FeaturedRecords)
                    .AsNoTracking()
                    .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
                    .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
                    .ToListAsync();

                children = children.Where(e => visited.Add(e.Id)).ToList();
                if (children.Count == 0)
                    break;

                children = await ps.LoadPostInfo(children, currentUser, true);
                await pcs.LoadPublisherCollectionsAsync(children);

                foreach (var child in children)
                {
                    if (child.RepliedPostId is not { } parentId)
                        continue;

                    if (!repliesByParent.TryGetValue(parentId, out var siblings))
                    {
                        siblings = [];
                        repliesByParent[parentId] = siblings;
                    }

                    siblings.Add(child);
                }

                frontier = children.Select(e => e.Id).ToList();
            }

            foreach (var root in rootReplies)
                FlattenThreadedReplies(root, repliesByParent, 0, descendants);

            if (descendants.Count > take)
            {
                hasMore = true;
                descendants = descendants.Take(take).ToList();
            }
        }

        if (currentUser != null)
            await ps.IncreaseViewCount(currentPost.Id, currentUser.Id, isDetailView: true);
        else
            await ps.IncreaseViewCount(currentPost.Id, isDetailView: true);

        var ancestorCount = ancestorNodes?.Count ?? 0;
        return Ok(new PostThreadResponse
        {
            Ancestors = ancestorNodes,
            Current = new ThreadedReplyNode
            {
                Post = currentPost,
                Depth = ancestorCount,
                ParentId = currentPost.RepliedPostId
            },
            Descendants = descendants,
            HasMore = hasMore
        });
    }

    [HttpGet("{id:guid}/forwards")]
    public async Task<ActionResult<List<SnPost>>> ListForwards(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as DyAccount;

        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(
                new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
            );
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null
            ? []
            : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var parent = await db.Posts.Where(e => e.Id == id).FirstOrDefaultAsync();
        if (parent is null)
            return NotFound();

        var (gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds) = await GetGatekeepInfoAsync(
            db.Posts.Where(e => e.ForwardedPostId == id && e.PublisherId != null).Select(e => e.PublisherId!.Value),
            currentUser
        );

        var totalCount = await db
            .Posts.Where(e => e.ForwardedPostId == parent.Id)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .CountAsync();

        var posts = await db
            .Posts.Where(e => e.ForwardedPostId == id)
            .Include(e => e.ForwardedPost)
            .Include(e => e.Categories)
            .Include(e => e.Tags)
            .Include(e => e.FeaturedRecords)
            .FilterWithVisibility(currentUser, userFriends, userPublishers, isListing: true, gatekeptPublisherIds, subscriberPublisherIds, closeFriendPublisherIds: closeFriendPublisherIds)
            .OrderByDescending(e => e.PublishedAt ?? e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        posts = await ps.LoadPostInfo(posts, currentUser, true);
        await pcs.LoadPublisherCollectionsAsync(posts);

        var postsId = posts.Select(e => e.Id).ToList();
        var reactionMaps = await ps.GetPostReactionMapBatch(postsId);
        foreach (var post in posts)
            post.ReactionsCount = reactionMaps.TryGetValue(post.Id, out var count)
                ? count
                : new Dictionary<string, int>();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(posts);
    }
}
