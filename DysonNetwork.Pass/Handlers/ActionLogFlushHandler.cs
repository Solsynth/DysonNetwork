using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using EFCore.BulkExtensions;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pass.Handlers;

public class ActionLogFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<SnActionLog>
{
    public async Task FlushAsync(IReadOnlyList<SnActionLog> items)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        var now = SystemClock.Instance.GetCurrentInstant();
        await db.BulkInsertAsync(items.Select(x =>
        {
            x.CreatedAt = now;
            x.UpdatedAt = x.CreatedAt;
            return x;
        }), config => config.ConflictOption = ConflictOption.Ignore);
    }
}

public class ActionLogFlushJob(FlushBufferService fbs, ActionLogFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}
