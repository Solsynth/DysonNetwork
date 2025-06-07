using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Sphere.Storage.Handlers;

public class LastActiveInfo
{
    public Auth.Session Session { get; set; }
    public Account.Account Account { get; set; }
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

        // Load all sessions that need to be updated in one batch
        var sessionsToUpdate = await db.AuthSessions
            .Where(s => sessionIdMap.Keys.Contains(s.Id))
            .ToListAsync();

        // Update their LastGrantedAt
        foreach (var session in sessionsToUpdate)
            session.LastGrantedAt = sessionIdMap[session.Id];

        // Bulk update sessions
        await db.BulkUpdateAsync(sessionsToUpdate);

        // Similarly, load account profiles in one batch
        var accountProfilesToUpdate = await db.AccountProfiles
            .Where(a => accountIdMap.Keys.Contains(a.AccountId))
            .ToListAsync();

        // Update their LastSeenAt
        foreach (var profile in accountProfilesToUpdate)
            profile.LastSeenAt = accountIdMap[profile.AccountId];

        // Bulk update profiles
        await db.BulkUpdateAsync(accountProfilesToUpdate);
    }
}

public class LastActiveFlushJob(FlushBufferService fbs, ActionLogFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}