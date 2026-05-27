using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace DysonNetwork.Sphere.Sticker;

public class StickerPackPopularityIncrement
{
    public Guid PackId { get; set; }
}

public class StickerPopularityFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<StickerPackPopularityIncrement>
{
    public async Task FlushAsync(IReadOnlyList<StickerPackPopularityIncrement> items)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        var lookupCounts = items
            .GroupBy(x => x.PackId)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (packId, count) in lookupCounts)
        {
            await db.StickerPacks
                .Where(p => p.Id == packId)
                .ExecuteUpdateAsync(p => p
                    .SetProperty(x => x.Popularity, x => x.Popularity + count));
        }
    }
}

public class StickerPopularityFlushJob(FlushBufferService fbs, StickerPopularityFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}
