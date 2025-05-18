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
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        await db.BulkInsertAsync(items);
    }
}

public class ReadReceiptFlushJob(FlushBufferService fbs, MessageReadReceiptFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}

public class ReadReceiptRecyclingJob(AppDatabase db) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var cutoff = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromDays(30));
        await db.ChatReadReceipts
            .Where(r => r.CreatedAt < cutoff)
            .ExecuteDeleteAsync();
    }
}