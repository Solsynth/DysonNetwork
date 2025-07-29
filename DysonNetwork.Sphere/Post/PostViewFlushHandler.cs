using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace DysonNetwork.Sphere.Post;

public class PostViewFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<PostViewInfo>
{
    public async Task FlushAsync(IReadOnlyList<Sphere.Post.PostViewInfo> items)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        // Group views by post
        var postViews = items
            .GroupBy(x => x.PostId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Calculate total views and unique views per post
        foreach (var postId in postViews.Keys)
        {
            // Calculate unique views by distinct viewer IDs (not null)
            var uniqueViews = postViews[postId]
                .Where(v => !string.IsNullOrEmpty(v.ViewerId))
                .Select(v => v.ViewerId)
                .Distinct()
                .Count();

            // Total views is just the count of all items for this post
            var totalViews = postViews[postId].Count;

            // Update the post in the database
            await db.Posts
                .Where(p => p.Id == postId)
                .ExecuteUpdateAsync(p => p
                    .SetProperty(x => x.ViewsTotal, x => x.ViewsTotal + totalViews)
                    .SetProperty(x => x.ViewsUnique, x => x.ViewsUnique + uniqueViews));

            // Invalidate any cache entries for this post
            await cache.RemoveAsync($"post:{postId}");
        }
    }
}

public class PostViewFlushJob(FlushBufferService fbs, PostViewFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}