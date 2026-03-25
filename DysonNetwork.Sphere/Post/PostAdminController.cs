using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/admin/posts")]
[Authorize]
public class PostAdminController(AppDatabase db) : ControllerBase
{
    [HttpGet("{id:guid}/lock")]
    public async Task<ActionResult> GetLockStatus(Guid id)
    {
        var post = await db.Posts.FindAsync(id);
        if (post is null)
            return NotFound();

        return Ok(new { locked = post.LockedAt.HasValue, lockedAt = post.LockedAt });
    }

    [HttpPost("{id:guid}/lock")]
    public async Task<ActionResult> LockPost(Guid id)
    {
        var post = await db.Posts.FindAsync(id);
        if (post is null)
            return NotFound();

        if (post.LockedAt is not null)
            return BadRequest("Post is already locked.");

        post.LockedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return Ok(new { locked = true, lockedAt = post.LockedAt });
    }

    [HttpDelete("{id:guid}/lock")]
    public async Task<ActionResult> UnlockPost(Guid id)
    {
        var post = await db.Posts.FindAsync(id);
        if (post is null)
            return NotFound();

        if (post.LockedAt is null)
            return BadRequest("Post is not locked.");

        post.LockedAt = null;
        await db.SaveChangesAsync();

        return Ok(new { locked = false });
    }

    [HttpPost("{id:guid}/lock/batch")]
    public async Task<ActionResult> LockPostsBatch([FromBody] List<Guid> ids)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var posts = await db.Posts.Where(p => ids.Contains(p.Id)).ToListAsync();

        foreach (var post in posts)
        {
            post.LockedAt ??= now;
        }

        await db.SaveChangesAsync();

        return Ok(new { locked = posts.Count });
    }

    [HttpDelete("lock/batch")]
    public async Task<ActionResult> UnlockPostsBatch([FromBody] List<Guid> ids)
    {
        var posts = await db.Posts.Where(p => ids.Contains(p.Id)).ToListAsync();

        foreach (var post in posts)
        {
            post.LockedAt = null;
        }

        await db.SaveChangesAsync();

        return Ok(new { unlocked = posts.Count });
    }
}