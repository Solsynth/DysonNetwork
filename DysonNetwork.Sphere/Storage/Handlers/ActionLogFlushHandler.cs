using DysonNetwork.Sphere.Account;
using EFCore.BulkExtensions;
using Quartz;

namespace DysonNetwork.Sphere.Storage.Handlers;

public class ActionLogFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<ActionLog>
{
    public async Task FlushAsync(IReadOnlyList<ActionLog> items)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        await db.BulkInsertAsync(items, config => config.ConflictOption = ConflictOption.Ignore);
    }
}

public class ActionLogFlushJob(FlushBufferService fbs, ActionLogFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}