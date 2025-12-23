using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

[Route(("/api/categories"))]
[ApiController]
public class PostCategorySubController(AppDatabase db) : ControllerBase
{
    /// <summary>
    /// Get all subscriptions of categories and tags for the current user
    /// </summary>
    /// <returns>List of active subscription</returns>
    [HttpGet("subscriptions")]
    [Authorize]
    public async Task<ActionResult<List<SnPostCategorySubscription>>> ListCategoriesSubscription(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var pubQuery = db.PostCategorySubscriptions
            .Include(ps => ps.Tag)
            .Include(ps => ps.Category)
            .Where(ps => ps.AccountId == accountId)
            .OrderByDescending(ps => ps.CreatedAt)
            .AsQueryable();

        var totalCount = await pubQuery.CountAsync();
        var subscriptions = await pubQuery
            .Take(take)
            .Skip(offset)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        return Ok(subscriptions);
    }
}