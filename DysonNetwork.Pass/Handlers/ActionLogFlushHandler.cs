using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Cache;
using EFCore.BulkExtensions;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pass.Handlers;

public class ActionLogFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<ActionLog>
{
    public async Task FlushAsync(IReadOnlyList<ActionLog> items)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        
        await db.BulkInsertAsync(items.Select(x =>
        {
            x.CreatedAt = SystemClock.Instance.GetCurrentInstant();
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