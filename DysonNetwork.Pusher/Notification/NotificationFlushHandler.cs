using DysonNetwork.Shared.Cache;
using EFCore.BulkExtensions;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pusher.Notification;

public class NotificationFlushHandler(AppDatabase db) : IFlushHandler<Notification>
{
    public async Task FlushAsync(IReadOnlyList<Notification> items)
    {
        await db.BulkInsertAsync(items.Select(x =>
        {
            x.CreatedAt = SystemClock.Instance.GetCurrentInstant();
            x.UpdatedAt = x.CreatedAt;
            return x;
        }), config => config.ConflictOption = ConflictOption.Ignore);
    }
}

public class NotificationFlushJob(FlushBufferService fbs, NotificationFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}
