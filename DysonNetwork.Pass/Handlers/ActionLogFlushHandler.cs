using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pass.Handlers;

public class ActionLogFlushHandler(IServiceProvider sp) : IFlushHandler<SnActionLog>
{
    public async Task FlushAsync(IReadOnlyList<SnActionLog> items)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        var now = SystemClock.Instance.GetCurrentInstant();
        foreach (var item in items)
        {
            item.CreatedAt = now;
            item.UpdatedAt = now;
        }
        db.ActionLogs.AddRange(items);
        await db.SaveChangesAsync();
    }
}

public class ActionLogFlushJob(FlushBufferService fbs, ActionLogFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}
