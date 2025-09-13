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

public class LastActiveFlushHandler(IServiceProvider srp, ILogger<LastActiveFlushHandler> logger)
    : IFlushHandler<LastActiveInfo>
{
    public async Task FlushAsync(IReadOnlyList<LastActiveInfo> items)
    {
        logger.LogInformation("Flushing {Count} LastActiveInfo items...", items.Count);

        using var scope = srp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

        // Remove duplicates by grouping on (sessionId, accountId), taking the most recent SeenAt
        var distinctItems = items
            .GroupBy(x => (SessionId: x.Session.Id, AccountId: x.Account.Id))
            .Select(g => g.OrderByDescending(x => x.SeenAt).First())
            .ToList();

        // Build dictionaries so we can match session/account IDs to their new "last seen" timestamps
        var sessionMap = distinctItems
            .GroupBy(x => x.Session.Id)
            .ToDictionary(g => g.Key, g => g.Last().SeenAt);

        var accountMap = distinctItems
            .GroupBy(x => x.Account.Id)
            .ToDictionary(g => g.Key, g => g.Last().SeenAt);

        var now = SystemClock.Instance.GetCurrentInstant();

        var updatingSessions = sessionMap.Select(x => x.Key).ToList();
        var sessionUpdates = await db.AuthSessions
            .Where(s => updatingSessions.Contains(s.Id))
            .ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.LastGrantedAt, now)
            );
        logger.LogInformation("Updated {Count} auth sessions according to LastActiveInfo", sessionUpdates);
        var newExpiration = now.Plus(Duration.FromDays(7));
        var keepAliveSessionUpdates = await db.AuthSessions
            .Where(s => updatingSessions.Contains(s.Id) && s.ExpiredAt != null)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(x => x.ExpiredAt, newExpiration)
            );
        logger.LogInformation("Updated {Count} auth sessions' duration according to LastActiveInfo", sessionUpdates);

        var updatingAccounts = accountMap.Select(x => x.Key).ToList();
        var profileUpdates = await db.AccountProfiles
            .Where(a => updatingAccounts.Contains(a.AccountId))
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.LastSeenAt, now));
        logger.LogInformation("Updated {Count} account profiles according to LastActiveInfo", profileUpdates);
    }
}

public class LastActiveFlushJob(FlushBufferService fbs, LastActiveFlushHandler hdl, ILogger<LastActiveFlushJob> logger)
    : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        try
        {
            logger.LogInformation("Running LastActiveInfo flush job...");
            await fbs.FlushAsync(hdl);
            logger.LogInformation("Completed LastActiveInfo flush job...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error running LastActiveInfo job...");
        }
    }
}