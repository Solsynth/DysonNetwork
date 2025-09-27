using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
public class PostCategoryController(AppDatabase db) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<List<SnPostCategory>>> ListCategories(
        [FromQuery] string? query = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] string? order = null
    )
    {
        var categoriesQuery = db.PostCategories
            .OrderBy(e => e.Name)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query))
            categoriesQuery = categoriesQuery
                .Where(e => EF.Functions.ILike(e.Slug, $"%{query}%"));
        if (!string.IsNullOrEmpty(order))
        {
            categoriesQuery = order switch
            {
                "usage" => categoriesQuery.OrderByDescending(e => e.Posts.Count),
                _ => categoriesQuery.OrderByDescending(e => e.CreatedAt)
            };
        }

        var totalCount = await categoriesQuery.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        // Get categories with their post counts in a single query
        var categories = await categoriesQuery
            .Skip(offset)
            .Take(take)
            .Select(c => new
            {
                Category = c,
                PostCount = c.Posts.Count
            })
            .ToListAsync();

        // Project results back to the original type and set the Usage property
        var result = categories.Select(x =>
        {
            x.Category.Usage = x.PostCount;
            return x.Category;
        }).ToList();

        return Ok(result);
    }

    [HttpGet("tags")]
    public async Task<ActionResult<List<SnPostTag>>> ListTags(
        [FromQuery] string? query = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] string? order = null
    )
    {
        var tagsQuery = db.PostTags
            .OrderBy(e => e.Name)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query))
            tagsQuery = tagsQuery
                .Where(e => EF.Functions.ILike(e.Slug, $"%{query}%"));
        if (!string.IsNullOrEmpty(order))
        {
            tagsQuery = order switch
            {
                "usage" => tagsQuery.OrderByDescending(e => e.Posts.Count),
                _ => tagsQuery.OrderByDescending(e => e.CreatedAt)
            };
        }

        var totalCount = await tagsQuery.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());

        // Get tags with their post counts in a single query
        var tags = await tagsQuery
            .Skip(offset)
            .Take(take)
            .Select(t => new
            {
                Tag = t,
                PostCount = t.Posts.Count
            })
            .ToListAsync();

        // Project results back to the original type and set the Usage property
        var result = tags.Select(x =>
        {
            x.Tag.Usage = x.PostCount;
            return x.Tag;
        }).ToList();

        return Ok(result);
    }

    [HttpGet("categories/{slug}")]
    public async Task<ActionResult<SnPostCategory>> GetCategory(string slug)
    {
        var category = await db.PostCategories.FirstOrDefaultAsync(e => e.Slug == slug);
        if (category is null)
            return NotFound();
        return Ok(category);
    }

    [HttpGet("tags/{slug}")]
    public async Task<ActionResult<SnPostTag>> GetTag(string slug)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(e => e.Slug == slug);
        if (tag is null)
            return NotFound();
        return Ok(tag);
    }

    [HttpPost("categories/{slug}/subscribe")]
    [Authorize]
    public async Task<ActionResult<SnPostCategorySubscription>> SubscribeCategory(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var category = await db.PostCategories.FirstOrDefaultAsync(c => c.Slug == slug);
        if (category == null)
        {
            return NotFound("Category not found.");
        }

        var existingSubscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.CategoryId == category.Id && s.AccountId == accountId);

        if (existingSubscription != null)
            return Ok(existingSubscription);

        var subscription = new SnPostCategorySubscription
        {
            AccountId = accountId,
            CategoryId = category.Id
        };

        db.PostCategorySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategorySubscription), new { slug }, subscription);
    }

    [HttpPost("categories/{slug}/unsubscribe")]
    [Authorize]
    public async Task<IActionResult> UnsubscribeCategory(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var category = await db.PostCategories.FirstOrDefaultAsync(c => c.Slug == slug);
        if (category == null)
            return NotFound("Category not found.");

        var subscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.CategoryId == category.Id && s.AccountId == accountId);

        if (subscription == null)
            return NoContent();

        db.PostCategorySubscriptions.Remove(subscription);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("categories/{slug}/subscription")]
    [Authorize]
    public async Task<ActionResult<SnPostCategorySubscription>> GetCategorySubscription(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var category = await db.PostCategories.FirstOrDefaultAsync(c => c.Slug == slug);
        if (category == null)
            return NotFound("Category not found.");

        var subscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.CategoryId == category.Id && s.AccountId == accountId);

        if (subscription == null)
            return NotFound("Subscription not found.");

        return Ok(subscription);
    }

    [HttpPost("tags/{slug}/subscribe")]
    [Authorize]
    public async Task<ActionResult<SnPostCategorySubscription>> SubscribeTag(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tag == null)
        {
            return NotFound("Tag not found.");
        }

        var existingSubscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.TagId == tag.Id && s.AccountId == accountId);

        if (existingSubscription != null)
        {
            return Ok(existingSubscription);
        }

        var subscription = new SnPostCategorySubscription
        {
            AccountId = accountId,
            TagId = tag.Id
        };

        db.PostCategorySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTagSubscription), new { slug }, subscription);
    }

    [HttpPost("tags/{slug}/unsubscribe")]
    [Authorize]
    public async Task<IActionResult> UnsubscribeTag(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tag == null)
        {
            return NotFound("Tag not found.");
        }

        var subscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.TagId == tag.Id && s.AccountId == accountId);

        if (subscription == null)
        {
            return NoContent();
        }

        db.PostCategorySubscriptions.Remove(subscription);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("tags/{slug}/subscription")]
    [Authorize]
    public async Task<ActionResult<SnPostCategorySubscription>> GetTagSubscription(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var tag = await db.PostTags.FirstOrDefaultAsync(t => t.Slug == slug);
        if (tag == null)
        {
            return NotFound("Tag not found.");
        }

        var subscription = await db.PostCategorySubscriptions
            .FirstOrDefaultAsync(s => s.TagId == tag.Id && s.AccountId == accountId);

        if (subscription == null)
        {
            return NotFound("Subscription not found.");
        }

        return Ok(subscription);
    }
}
