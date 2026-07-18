using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/publishers/{publisherName}/collections")]
[ApiFeature("posts.collections", Revision = 1)]
public class PostCollectionController(
    AppDatabase db,
    PostCollectionService collectionService,
    PublisherService publisherService,
    DyProfileService.DyProfileServiceClient accounts,
    DyFileService.DyFileServiceClient files
) : ControllerBase
{
    public class CreateCollectionRequest
    {
        [MaxLength(128)] public string Slug { get; set; } = null!;
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? BackgroundId { get; set; }
        public string? IconId { get; set; }
    }

    public class UpdateCollectionRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? BackgroundId { get; set; }
        public string? IconId { get; set; }
    }

    public class AddCollectionPostRequest
    {
        public Guid PostId { get; set; }
        public int? Order { get; set; }
    }

    public class BatchAddCollectionPostsRequest
    {
        public List<Guid> PostIds { get; set; } = [];
    }

    public class BatchRemoveCollectionPostsRequest
    {
        public List<Guid> PostIds { get; set; } = [];
    }

    public class ReorderCollectionPostsRequest
    {
        public List<Guid> PostIds { get; set; } = [];
    }

    private sealed record CollectionSearchContext(string Query, bool UseFuzzyMatch);

    private static CollectionSearchContext? CreateSearchContext(string? query)
    {
        var normalized = query?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : new CollectionSearchContext(normalized, normalized.Length >= 3);
    }

    private static IQueryable<SnPostCollection> ApplyCollectionSearch(
        IQueryable<SnPostCollection> query,
        CollectionSearchContext? searchContext
    )
    {
        if (searchContext is null)
            return query;

        var searchPattern = $"%{searchContext.Query}%";
        if (!searchContext.UseFuzzyMatch)
        {
            return query.Where(c =>
                EF.Functions.ILike(c.Slug, searchPattern)
                || (c.Name != null && EF.Functions.ILike(c.Name, searchPattern))
                || (c.Description != null && EF.Functions.ILike(c.Description, searchPattern))
                || EF.Functions.ILike(c.Publisher.Name, searchPattern)
                || EF.Functions.ILike(c.Publisher.Nick, searchPattern)
            );
        }

        return query.Where(c =>
            EF.Functions.ILike(c.Slug, searchPattern)
            || (c.Name != null && EF.Functions.ILike(c.Name, searchPattern))
            || (c.Description != null && EF.Functions.ILike(c.Description, searchPattern))
            || EF.Functions.ILike(c.Publisher.Name, searchPattern)
            || EF.Functions.ILike(c.Publisher.Nick, searchPattern)
            || EF.Functions.TrigramsAreSimilar(c.Slug, searchContext.Query)
            || (c.Name != null && EF.Functions.TrigramsAreSimilar(c.Name, searchContext.Query))
            || (c.Description != null && EF.Functions.TrigramsAreWordSimilar(searchContext.Query, c.Description))
            || EF.Functions.TrigramsAreSimilar(c.Publisher.Name, searchContext.Query)
            || EF.Functions.TrigramsAreSimilar(c.Publisher.Nick, searchContext.Query)
        );
    }

    private static IOrderedQueryable<SnPostCollection> ApplyCollectionSearchOrdering(
        IQueryable<SnPostCollection> query,
        CollectionSearchContext? searchContext
    )
    {
        if (searchContext is not { UseFuzzyMatch: true })
            return query.OrderBy(c => c.Name ?? c.Slug).ThenBy(c => c.Slug);

        var searchPattern = $"%{searchContext.Query}%";
        return query
            .OrderByDescending(c =>
                EF.Functions.ILike(c.Slug, searchPattern)
                || (c.Name != null && EF.Functions.ILike(c.Name, searchPattern))
                || (c.Description != null && EF.Functions.ILike(c.Description, searchPattern))
                || EF.Functions.ILike(c.Publisher.Name, searchPattern)
                || EF.Functions.ILike(c.Publisher.Nick, searchPattern)
            )
            .ThenByDescending(c => EF.Functions.TrigramsSimilarity(c.Slug, searchContext.Query))
            .ThenByDescending(c => c.Name != null ? EF.Functions.TrigramsSimilarity(c.Name, searchContext.Query) : 0.0f)
            .ThenByDescending(c => c.Description != null ? EF.Functions.TrigramsWordSimilarity(searchContext.Query, c.Description) : 0.0f)
            .ThenByDescending(c => EF.Functions.TrigramsSimilarity(c.Publisher.Name, searchContext.Query))
            .ThenByDescending(c => EF.Functions.TrigramsSimilarity(c.Publisher.Nick, searchContext.Query))
            .ThenBy(c => c.Name ?? c.Slug)
            .ThenBy(c => c.Slug);
    }

    [HttpGet("/api/collections")]
    public async Task<ActionResult<List<SnPostCollection>>> ListAllCollections(
        [FromQuery] string? query = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        take = Math.Clamp(take, 1, 100);
        offset = Math.Max(0, offset);

        var searchContext = CreateSearchContext(query);

        var collectionsQuery = db.PostCollections
            .AsNoTracking()
            .Include(c => c.Publisher)
            .AsQueryable();

        collectionsQuery = ApplyCollectionSearch(collectionsQuery, searchContext);
        collectionsQuery = ApplyCollectionSearchOrdering(collectionsQuery, searchContext);

        var totalCount = await collectionsQuery.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        var collections = await collectionsQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(collections);
    }

    [HttpGet]
    public async Task<ActionResult<List<SnPostCollection>>> ListCollections(string publisherName)
    {
        var collections = await collectionService.ListCollectionsAsync(publisherName);
        return Ok(collections);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnPostCollection>> CreateCollection(
        string publisherName,
        [FromBody] CreateCollectionRequest request
    )
    {
        var publisher = await RequirePublisherEditorAsync(publisherName);
        if (publisher.Result is not null)
            return publisher.Result;

        try
        {
            SnCloudFileReferenceObject? background = null, icon = null;
            if (request.BackgroundId is not null)
            {
                var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
                background = SnCloudFileReferenceObject.FromProtoValue(file);
            }
            if (request.IconId is not null)
            {
                var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.IconId });
                icon = SnCloudFileReferenceObject.FromProtoValue(file);
            }

            var collection = await collectionService.CreateCollectionAsync(
                publisher.Value!,
                request.Slug,
                request.Name,
                request.Description,
                background,
                icon
            );
            return CreatedAtAction(nameof(GetCollection), new { publisherName, slug = collection.Slug }, collection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "COLLECTION_CREATE_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<SnPostCollection>> GetCollection(string publisherName, string slug)
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();
        return Ok(collection);
    }

    [HttpPost("{slug}/subscribe")]
    [AskPermission(PermissionKeys.PostCollectionsPostsManage)]
    [Authorize]
    public async Task<ActionResult<SnPostCategorySubscription>> SubscribeCollection(string publisherName, string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound(new ApiError { Code = "COLLECTION_NOT_FOUND", Message = "Collection not found.", Status = 404 });

        var existingSubscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.CollectionId == collection.Id && s.AccountId == accountId);
        if (existingSubscription is not null)
            return Ok(existingSubscription);

        var subscription = new SnPostCategorySubscription
        {
            Id = Guid.NewGuid(),
            AccountId = accountId,
            CollectionId = collection.Id,
        };

        db.PostCategorySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return CreatedAtAction(
            nameof(GetCollectionSubscription),
            new { publisherName, slug },
            subscription
        );
    }

    [HttpPost("{slug}/unsubscribe")]
    [AskPermission(PermissionKeys.PostCollectionsPostsManage)]
    [Authorize]
    public async Task<IActionResult> UnsubscribeCollection(string publisherName, string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound(new ApiError { Code = "COLLECTION_NOT_FOUND", Message = "Collection not found.", Status = 404 });

        var subscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.CollectionId == collection.Id && s.AccountId == accountId);
        if (subscription is null)
            return NoContent();

        db.PostCategorySubscriptions.Remove(subscription);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{slug}/subscription")]
    [Authorize]
    public async Task<ActionResult<SnPostCategorySubscription>> GetCollectionSubscription(
        string publisherName,
        string slug
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound(new ApiError { Code = "COLLECTION_NOT_FOUND", Message = "Collection not found.", Status = 404 });

        var subscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.CollectionId == collection.Id && s.AccountId == accountId);
        if (subscription is null)
            return NotFound(new ApiError { Code = "COLLECTION_SUBSCRIPTION_NOT_FOUND", Message = "Subscription not found.", Status = 404 });

        return Ok(subscription);
    }

    [HttpPatch("{slug}")]
    [Authorize]
    public async Task<ActionResult<SnPostCollection>> UpdateCollection(
        string publisherName,
        string slug,
        [FromBody] UpdateCollectionRequest request
    )
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var auth = await RequirePublisherEditorAsync(collection.Publisher.Name);
        if (auth.Result is not null)
            return auth.Result;

        SnCloudFileReferenceObject? background = null, icon = null;
        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            background = SnCloudFileReferenceObject.FromProtoValue(file);
        }
        if (request.IconId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.IconId });
            icon = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        collection = await collectionService.UpdateCollectionAsync(collection, request.Name, request.Description, background, icon);
        return Ok(collection);
    }

    [HttpDelete("{slug}")]
    [Authorize]
    public async Task<ActionResult> DeleteCollection(string publisherName, string slug)
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var auth = await RequirePublisherEditorAsync(collection.Publisher.Name);
        if (auth.Result is not null)
            return auth.Result;

        await collectionService.DeleteCollectionAsync(collection);
        return NoContent();
    }

    [HttpGet("{slug}/posts")]
    public async Task<ActionResult<List<SnPost>>> ListCollectionPosts(
        string publisherName,
        string slug,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        var userFriends = await GetUserFriendsAsync(currentUser);
        var userPublishers = currentUser is null ? [] : await publisherService.GetUserPublishers(Guid.Parse(currentUser.Id));

        var totalCount = await collectionService.CountVisiblePostsAsync(collection, currentUser, userFriends, userPublishers);
        var posts = await collectionService.ListPostsAsync(collection, currentUser, userFriends, userPublishers, offset, take);
        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(posts);
    }

    [HttpPost("{slug}/posts")]
    [AskPermission(PermissionKeys.PostCollectionsPostsManage)]
    [Authorize]
    public async Task<ActionResult> AddCollectionPost(
        string publisherName,
        string slug,
        [FromBody] AddCollectionPostRequest request
    )
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var auth = await RequirePublisherEditorAsync(collection.Publisher.Name);
        if (auth.Result is not null)
            return auth.Result;

        if (request.Order.HasValue)
            return BadRequest(new ApiError { Code = "COLLECTION_ORDER_NOT_SUPPORTED", Message = "Manual ordering is not supported. Collection posts are sorted automatically by published date descending.", Status = 400 });

        try
        {
            await collectionService.AddPostAsync(collection, request.PostId, null);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "COLLECTION_ADD_POST_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("{slug}/posts/batch")]
    [AskPermission(PermissionKeys.PostCollectionsPostsManage)]
    [Authorize]
    public async Task<ActionResult> BatchAddCollectionPosts(
        string publisherName,
        string slug,
        [FromBody] BatchAddCollectionPostsRequest request
    )
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var auth = await RequirePublisherEditorAsync(collection.Publisher.Name);
        if (auth.Result is not null)
            return auth.Result;

        try
        {
            await collectionService.BatchAddPostsAsync(collection, request.PostIds);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "COLLECTION_BATCH_ADD_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("{slug}/posts/batch/remove")]
    [AskPermission(PermissionKeys.PostCollectionsPostsManage)]
    [Authorize]
    public async Task<ActionResult> BatchRemoveCollectionPosts(
        string publisherName,
        string slug,
        [FromBody] BatchRemoveCollectionPostsRequest request
    )
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var auth = await RequirePublisherEditorAsync(collection.Publisher.Name);
        if (auth.Result is not null)
            return auth.Result;

        await collectionService.BatchRemovePostsAsync(collection, request.PostIds);
        return NoContent();
    }

    [HttpDelete("{slug}/posts/{postId:guid}")]
    [AskPermission(PermissionKeys.PostCollectionsPostsManage)]
    [Authorize]
    public async Task<ActionResult> RemoveCollectionPost(string publisherName, string slug, Guid postId)
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var auth = await RequirePublisherEditorAsync(collection.Publisher.Name);
        if (auth.Result is not null)
            return auth.Result;

        await collectionService.RemovePostAsync(collection, postId);
        return NoContent();
    }

    [HttpPut("{slug}/posts/reorder")]
    [AskPermission(PermissionKeys.PostCollectionsPostsManage)]
    [Authorize]
    public async Task<ActionResult> ReorderCollectionPosts(
        string publisherName,
        string slug,
        [FromBody] ReorderCollectionPostsRequest request
    )
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var auth = await RequirePublisherEditorAsync(collection.Publisher.Name);
        if (auth.Result is not null)
            return auth.Result;

        return BadRequest(new ApiError { Code = "COLLECTION_REORDER_NOT_SUPPORTED", Message = "Manual reordering is not supported. Collection posts are sorted automatically by published date descending.", Status = 400 });
    }

    [HttpGet("{slug}/posts/{postId:guid}/prev")]
    public async Task<ActionResult<SnPost>> GetPrevCollectionPost(string publisherName, string slug, Guid postId)
    {
        return await GetAdjacentCollectionPost(publisherName, slug, postId, next: false);
    }

    [HttpGet("{slug}/posts/{postId:guid}/next")]
    public async Task<ActionResult<SnPost>> GetNextCollectionPost(string publisherName, string slug, Guid postId)
    {
        return await GetAdjacentCollectionPost(publisherName, slug, postId, next: true);
    }

    private async Task<ActionResult<SnPost>> GetAdjacentCollectionPost(
        string publisherName,
        string slug,
        Guid postId,
        bool next
    )
    {
        var collection = await collectionService.GetCollectionBySlugAsync(publisherName, slug);
        if (collection is null)
            return NotFound();

        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        var userFriends = await GetUserFriendsAsync(currentUser);
        var userPublishers = currentUser is null ? [] : await publisherService.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await collectionService.GetAdjacentPostAsync(
            collection,
            postId,
            next,
            currentUser,
            userFriends,
            userPublishers
        );

        if (post is null)
            return NotFound(new ApiError { Code = "COLLECTION_POST_NOT_FOUND", Message = next ? "No next post found." : "No previous post found.", Status = 404 });
        return Ok(post);
    }

    private async Task<List<Guid>> GetUserFriendsAsync(DyAccount? currentUser)
    {
        if (currentUser is null)
            return [];

        var friendsResponse = await accounts.ListFriendsAsync(
            new DyListRelationshipSimpleRequest { RelatedId = currentUser.Id }
        );
        return friendsResponse.AccountsId.Select(Guid.Parse).ToList();
    }

    private async Task<ActionResult<SnPublisher?>> RequirePublisherEditorAsync(string publisherName)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var publisher = await publisherService.GetPublisherByName(publisherName);
        if (publisher is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await publisherService.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, ApiError.Unauthorized("You need at least be an editor to manage this publisher collections.", forbidden: true));

        return publisher;
    }
}
