using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/admin/collections")]
[Authorize]
[ApiFeature("admin.collections", Revision = 1)]
public class PostCollectionAdminController(
    AppDatabase db,
    PostCollectionService collectionService,
    RemoteActionLogService als
) : ControllerBase
{
    public class UpdateCollectionRequest
    {
        [MaxLength(256)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<List<SnPostCollection>>> ListCollections(
        [FromQuery] string? query = null,
        [FromQuery] Guid? publisherId = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var collectionsQuery = db.PostCollections
            .AsNoTracking()
            .Include(c => c.Publisher)
            .AsQueryable();

        if (publisherId.HasValue)
            collectionsQuery = collectionsQuery.Where(c => c.PublisherId == publisherId.Value);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            collectionsQuery = collectionsQuery.Where(c =>
                EF.Functions.ILike(c.Slug, $"%{probe}%") ||
                (c.Name != null && EF.Functions.ILike(c.Name, $"%{probe}%")) ||
                (c.Description != null && EF.Functions.ILike(c.Description, $"%{probe}%")) ||
                EF.Functions.ILike(c.Publisher.Name, $"%{probe}%") ||
                EF.Functions.ILike(c.Publisher.Nick, $"%{probe}%"));
        }

        var total = await collectionsQuery.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var collections = await collectionsQuery
            .OrderByDescending(c => c.UpdatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        var ids = collections.Select(c => c.Id).ToList();
        var itemCounts = await db.PostCollectionItems
            .AsNoTracking()
            .Where(i => ids.Contains(i.CollectionId))
            .GroupBy(i => i.CollectionId)
            .Select(g => new { CollectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectionId, x => x.Count, HttpContext.RequestAborted);

        foreach (var collection in collections)
            collection.ItemCount = itemCounts.GetValueOrDefault(collection.Id);

        return Ok(collections);
    }

    [HttpGet("{id:guid}")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPostCollection>> GetCollection(Guid id)
    {
        var collection = await db.PostCollections
            .AsNoTracking()
            .Include(c => c.Publisher)
            .FirstOrDefaultAsync(c => c.Id == id, HttpContext.RequestAborted);
        if (collection is null)
            return NotFound();

        collection.ItemCount = await db.PostCollectionItems
            .CountAsync(i => i.CollectionId == collection.Id, HttpContext.RequestAborted);

        return Ok(collection);
    }

    [HttpPatch("{id:guid}")]
    [AskPermission(PermissionKeys.PostCollectionsUpdate)]
    public async Task<ActionResult<SnPostCollection>> UpdateCollection(
        Guid id,
        [FromBody] UpdateCollectionRequest request
    )
    {
        var collection = await db.PostCollections
            .Include(c => c.Publisher)
            .FirstOrDefaultAsync(c => c.Id == id, HttpContext.RequestAborted);
        if (collection is null)
            return NotFound();

        try
        {
            collection = await collectionService.UpdateCollectionAsync(
                collection,
                request.Name ?? collection.Name,
                request.Description ?? collection.Description,
                collection.Background,
                collection.Icon
            );
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "COLLECTION_UPDATE_FAILED", Message = ex.Message, Status = 400 });
        }

        LogCollectionAction("update", collection.Id, new Dictionary<string, object>
        {
            ["slug"] = collection.Slug,
            ["publisher_id"] = collection.PublisherId.ToString()
        });

        return Ok(collection);
    }

    [HttpDelete("{id:guid}")]
    [AskPermission(PermissionKeys.PostCollectionsDelete)]
    public async Task<IActionResult> DeleteCollection(Guid id)
    {
        var collection = await db.PostCollections
            .FirstOrDefaultAsync(c => c.Id == id, HttpContext.RequestAborted);
        if (collection is null)
            return NotFound();

        var publisherId = collection.PublisherId;
        var slug = collection.Slug;

        await collectionService.DeleteCollectionAsync(collection);

        LogCollectionAction("delete", id, new Dictionary<string, object>
        {
            ["slug"] = slug,
            ["publisher_id"] = publisherId.ToString()
        });

        return NoContent();
    }

    private void LogCollectionAction(string operation, Guid collectionId, Dictionary<string, object>? extraMeta = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return;

        var meta = new Dictionary<string, object>
        {
            ["collection_id"] = collectionId.ToString(),
            ["operation"] = operation
        };
        if (extraMeta is not null)
        {
            foreach (var (key, value) in extraMeta)
                meta[key] = value;
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostCollectionsAdmin,
            meta,
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );
    }
}
