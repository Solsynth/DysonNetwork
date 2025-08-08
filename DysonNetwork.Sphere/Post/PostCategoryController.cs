using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/posts")]
public class PostCategoryController(AppDatabase db) : ControllerBase
{
    [HttpGet("categories")]
    public async Task<ActionResult<List<PostCategory>>> ListCategories(
        [FromQuery] string? query = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var categoriesQuery = db.PostCategories
            .OrderBy(e => e.Name)
            .AsQueryable();
        if (!string.IsNullOrEmpty(query))
            categoriesQuery = categoriesQuery
                .Where(e => EF.Functions.ILike(e.Slug, $"%{query}%"));
        var totalCount = await categoriesQuery.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());
        var categories = await categoriesQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        return Ok(categories);
    }

    [HttpGet("tags")]
    public async Task<ActionResult<List<PostTag>>> ListTags(
        [FromQuery] string? query = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        var tagsQuery = db.PostTags
            .OrderBy(e => e.Name)
            .AsQueryable();
        if (!string.IsNullOrEmpty(query))
            tagsQuery = tagsQuery
                .Where(e => EF.Functions.ILike(e.Slug, $"%{query}%"));
        var totalCount = await tagsQuery.CountAsync();
        Response.Headers.Append("X-Total", totalCount.ToString());
        var tags = await tagsQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(tags);
    }
    
    [HttpGet("categories/{slug}")]
    public async Task<ActionResult<PostCategory>> GetCategory(string slug)
    {
        var category = await db.PostCategories.FirstOrDefaultAsync(e => e.Slug == slug);
        if (category is null)
            return NotFound();
        return Ok(category);
    }
    
    [HttpGet("tags/{slug}")]
    public async Task<ActionResult<PostTag>> GetTag(string slug)
    {
        var tag = await db.PostTags.FirstOrDefaultAsync(e => e.Slug == slug);
        if (tag is null)
            return NotFound();
        return Ok(tag);
    }
}