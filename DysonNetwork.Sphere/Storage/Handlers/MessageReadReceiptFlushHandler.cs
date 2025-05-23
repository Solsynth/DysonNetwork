using DysonNetwork.Sphere.Chat;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Storage.Handlers;

public class MessageReadReceiptFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<MessageReadReceipt>
{
    public async Task FlushAsync(IReadOnlyList<MessageReadReceipt> items)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var distinctId = items
            .DistinctBy(x => x.SenderId)
            .Select(x => x.SenderId)
            .ToList();

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        await db.ChatMembers.Where(r => distinctId.Contains(r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastReadAt, now)
            );
    }
}

public class ReadReceiptFlushJob(FlushBufferService fbs, MessageReadReceiptFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}
