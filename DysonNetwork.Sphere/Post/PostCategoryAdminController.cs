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
[Route("/api/admin/categories")]
[Authorize]
[ApiFeature("admin.categories", Revision = 1)]
public class PostCategoryAdminController(
    AppDatabase db,
    RemoteActionLogService als
) : ControllerBase
{
    public class CreateCategoryRequest
    {
        [Required, MaxLength(128)] public string Slug { get; set; } = null!;
        [MaxLength(256)] public string? Name { get; set; }
    }

    public class UpdateCategoryRequest
    {
        [MaxLength(128)] public string? Slug { get; set; }
        [MaxLength(256)] public string? Name { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<List<SnPostCategory>>> ListCategories(
        [FromQuery] string? query = null,
        [FromQuery] string? order = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var categoriesQuery = db.PostCategories.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            categoriesQuery = categoriesQuery.Where(c =>
                EF.Functions.ILike(c.Slug, $"%{probe}%") ||
                (c.Name != null && EF.Functions.ILike(c.Name, $"%{probe}%")));
        }

        categoriesQuery = order switch
        {
            "usage" => categoriesQuery.OrderByDescending(c => c.Posts.Count).ThenBy(c => c.Slug),
            "name" => categoriesQuery.OrderBy(c => c.Name ?? c.Slug),
            "created" => categoriesQuery.OrderByDescending(c => c.CreatedAt),
            _ => categoriesQuery.OrderByDescending(c => c.UpdatedAt)
        };

        var total = await categoriesQuery.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var rows = await categoriesQuery
            .Skip(offset)
            .Take(take)
            .Select(c => new { Category = c, PostCount = c.Posts.Count })
            .ToListAsync(HttpContext.RequestAborted);

        var result = rows.Select(x =>
        {
            x.Category.Usage = x.PostCount;
            return x.Category;
        }).ToList();

        return Ok(result);
    }

    [HttpGet("{slug}")]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<SnPostCategory>> GetCategory(string slug)
    {
        var category = await db.PostCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (category is null)
            return NotFound();

        category.Usage = await db.Posts
            .Where(p => p.Categories.Any(c => c.Id == category.Id))
            .CountAsync(HttpContext.RequestAborted);

        return Ok(category);
    }

    [HttpPost]
    [AskPermission(PermissionKeys.PostCategoriesManage)]
    public async Task<ActionResult<SnPostCategory>> CreateCategory([FromBody] CreateCategoryRequest request)
    {
        var normalizedSlug = NormalizeSlug(request.Slug);
        if (string.IsNullOrWhiteSpace(normalizedSlug))
            return BadRequest(new ApiError { Code = "CATEGORY_SLUG_REQUIRED", Message = "Slug is required.", Status = 400 });

        var exists = await db.PostCategories.AnyAsync(
            c => c.Slug.ToLower() == normalizedSlug,
            HttpContext.RequestAborted
        );
        if (exists)
            return BadRequest(new ApiError { Code = "CATEGORY_SLUG_EXISTS", Message = "A category with this slug already exists.", Status = 400 });

        var category = new SnPostCategory
        {
            Slug = normalizedSlug,
            Name = request.Name
        };

        db.PostCategories.Add(category);
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogCategoryAction("create", category.Id, new Dictionary<string, object>
        {
            ["slug"] = category.Slug
        });

        return CreatedAtAction(nameof(GetCategory), new { slug = category.Slug }, category);
    }

    [HttpPatch("{slug}")]
    [AskPermission(PermissionKeys.PostCategoriesManage)]
    public async Task<ActionResult<SnPostCategory>> UpdateCategory(
        string slug,
        [FromBody] UpdateCategoryRequest request
    )
    {
        var category = await db.PostCategories
            .FirstOrDefaultAsync(c => c.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (category is null)
            return NotFound();

        if (request.Slug is not null)
        {
            var normalizedSlug = NormalizeSlug(request.Slug);
            if (string.IsNullOrWhiteSpace(normalizedSlug))
                return BadRequest(new ApiError { Code = "CATEGORY_SLUG_REQUIRED", Message = "Slug cannot be empty.", Status = 400 });

            if (normalizedSlug != category.Slug)
            {
                var exists = await db.PostCategories.AnyAsync(
                    c => c.Slug.ToLower() == normalizedSlug && c.Id != category.Id,
                    HttpContext.RequestAborted
                );
                if (exists)
                    return BadRequest(new ApiError { Code = "CATEGORY_SLUG_EXISTS", Message = "A category with this slug already exists.", Status = 400 });
                category.Slug = normalizedSlug;
            }
        }

        if (request.Name is not null)
            category.Name = request.Name;

        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogCategoryAction("update", category.Id, new Dictionary<string, object>
        {
            ["slug"] = category.Slug
        });

        return Ok(category);
    }

    [HttpDelete("{slug}")]
    [AskPermission(PermissionKeys.PostCategoriesManage)]
    public async Task<IActionResult> DeleteCategory(string slug)
    {
        var category = await db.PostCategories
            .Include(c => c.Posts)
            .FirstOrDefaultAsync(c => c.Slug.ToLower() == slug.ToLowerInvariant(), HttpContext.RequestAborted);
        if (category is null)
            return NotFound();

        // Detach posts before delete to avoid orphan link issues.
        category.Posts.Clear();

        var subscriptions = await db.PostCategorySubscriptions
            .Where(s => s.CategoryId == category.Id)
            .ToListAsync(HttpContext.RequestAborted);
        db.PostCategorySubscriptions.RemoveRange(subscriptions);

        db.PostCategories.Remove(category);
        await db.SaveChangesAsync(HttpContext.RequestAborted);

        LogCategoryAction("delete", category.Id, new Dictionary<string, object>
        {
            ["slug"] = category.Slug
        });

        return NoContent();
    }

    private static string NormalizeSlug(string value) => value.Trim().ToLowerInvariant();

    private void LogCategoryAction(string operation, Guid categoryId, Dictionary<string, object>? extraMeta = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return;

        var meta = new Dictionary<string, object>
        {
            ["category_id"] = categoryId.ToString(),
            ["operation"] = operation
        };
        if (extraMeta is not null)
        {
            foreach (var (key, value) in extraMeta)
                meta[key] = value;
        }

        als.CreateActionLog(
            Guid.Parse(currentUser.Id),
            ActionLogType.PostCategoriesManage,
            meta,
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );
    }
}
