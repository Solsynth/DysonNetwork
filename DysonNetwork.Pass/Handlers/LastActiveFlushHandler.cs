using DysonNetwork.Shared.Cache;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pass.Handlers;

public class LastActiveInfo
{
    public Auth.AuthSession Session { get; set; } = null!;
    public Account.Account Account { get; set; } = null!;
    public Instant SeenAt { get; set; }
}

public class LastActiveFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<LastActiveInfo>
{
    public async Task FlushAsync(IReadOnlyList<LastActiveInfo> items)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        // Remove duplicates by grouping on (sessionId, accountId), taking the most recent SeenAt
        var distinctItems = items
            .GroupBy(x => (SessionId: x.Session.Id, AccountId: x.Account.Id))
            .Select(g => g.OrderByDescending(x => x.SeenAt).First())
            .ToList();

        // Build dictionaries so we can match session/account IDs to their new "last seen" timestamps
        var sessionIdMap = distinctItems
            .GroupBy(x => x.Session.Id)
            .ToDictionary(g => g.Key, g => g.Last().SeenAt);

        var accountIdMap = distinctItems
            .GroupBy(x => x.Account.Id)
            .ToDictionary(g => g.Key, g => g.Last().SeenAt);

        // Update sessions using native EF Core ExecuteUpdateAsync
        foreach (var kvp in sessionIdMap)
        {
            await db.AuthSessions
                .Where(s => s.Id == kvp.Key)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastGrantedAt, kvp.Value));
        }

        // Update account profiles using native EF Core ExecuteUpdateAsync
        foreach (var kvp in accountIdMap)
        {
            await db.AccountProfiles
                .Where(a => a.AccountId == kvp.Key)
                .ExecuteUpdateAsync(a => a.SetProperty(x => x.LastSeenAt, kvp.Value));
        }
    }
}

public class LastActiveFlushJob(FlushBufferService fbs, ActionLogFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}