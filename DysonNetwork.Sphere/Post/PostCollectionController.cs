using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/publishers/{publisherName}/collections")]
public class PostCollectionController(
    AppDatabase db,
    PostCollectionService collectionService,
    PublisherService publisherService,
    DyProfileService.DyProfileServiceClient accounts
) : ControllerBase
{
    public class CreateCollectionRequest
    {
        [MaxLength(128)] public string Slug { get; set; } = null!;
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
    }

    public class UpdateCollectionRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
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

    public class ReorderCollectionPostsRequest
    {
        public List<Guid> PostIds { get; set; } = [];
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
            var collection = await collectionService.CreateCollectionAsync(
                publisher.Value!,
                request.Slug,
                request.Name,
                request.Description
            );
            return CreatedAtAction(nameof(GetCollection), new { publisherName, slug = collection.Slug }, collection);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
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

        collection = await collectionService.UpdateCollectionAsync(collection, request.Name, request.Description);
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

        try
        {
            await collectionService.AddPostAsync(collection, request.PostId, request.Order);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{slug}/posts/batch")]
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
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{slug}/posts/{postId:guid}")]
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

        try
        {
            await collectionService.ReorderPostsAsync(collection, request.PostIds);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
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
            return NotFound(next ? "No next post found" : "No previous post found");
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
            return Unauthorized();

        var publisher = await publisherService.GetPublisherByName(publisherName);
        if (publisher is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        if (!await publisherService.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to manage this publisher collections.");

        return publisher;
    }
}
