using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace DysonNetwork.Ring.Services;

public class PushSubRemovalRequest
{
    public Guid SubId { get; set; }
}

public class PushSubFlushHandler(IServiceProvider sp) : IFlushHandler<PushSubRemovalRequest>
{
    public async Task FlushAsync(IReadOnlyList<PushSubRemovalRequest> items)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PushSubFlushHandler>>();

        var tokenIds = items.Select(x => x.SubId).Distinct().ToList();

        var count = await db.PushSubscriptions
            .Where(s => tokenIds.Contains(s.Id))
            .ExecuteDeleteAsync();
        logger.LogInformation("Removed {Count} invalid push notification tokens...", count);
    }
}

public class PushSubFlushJob(FlushBufferService fbs, PushSubFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}
