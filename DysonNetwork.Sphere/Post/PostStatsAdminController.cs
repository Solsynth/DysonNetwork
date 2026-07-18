using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

[ApiController]
[Route("/api/admin/stats")]
[Authorize]
[ApiFeature("admin.stats", Revision = 1)]
public class PostStatsAdminController(AppDatabase db) : ControllerBase
{
    public class PostStatsResponse
    {
        public Instant CalculatedAt { get; set; }
        public long TotalPosts { get; set; }
        public long PublishedPosts { get; set; }
        public long DraftPosts { get; set; }
        public long PostsLastDay { get; set; }
        public long PostsLastWeek { get; set; }
        public long PostsLastMonth { get; set; }
        public long TotalPublishers { get; set; }
        public long TotalReactions { get; set; }
        public long TotalBookmarks { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.PostsModerate)]
    public async Task<ActionResult<PostStatsResponse>> GetStats(CancellationToken cancellationToken)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var oneDayAgo = now - Duration.FromDays(1);
        var sevenDaysAgo = now - Duration.FromDays(7);
        var thirtyDaysAgo = now - Duration.FromDays(30);
        var posts = db.Posts.AsNoTracking();

        return Ok(new PostStatsResponse
        {
            CalculatedAt = now,
            TotalPosts = await posts.LongCountAsync(cancellationToken),
            PublishedPosts = await posts.LongCountAsync(p => p.PublishedAt != null, cancellationToken),
            DraftPosts = await posts.LongCountAsync(p => p.DraftedAt != null, cancellationToken),
            PostsLastDay = await posts.LongCountAsync(p => p.CreatedAt >= oneDayAgo, cancellationToken),
            PostsLastWeek = await posts.LongCountAsync(p => p.CreatedAt >= sevenDaysAgo, cancellationToken),
            PostsLastMonth = await posts.LongCountAsync(p => p.CreatedAt >= thirtyDaysAgo, cancellationToken),
            TotalPublishers = await db.Publishers.AsNoTracking().LongCountAsync(cancellationToken),
            TotalReactions = await db.PostReactions.AsNoTracking().LongCountAsync(cancellationToken),
            TotalBookmarks = await db.PostBookmarks.AsNoTracking().LongCountAsync(cancellationToken)
        });
    }
}
