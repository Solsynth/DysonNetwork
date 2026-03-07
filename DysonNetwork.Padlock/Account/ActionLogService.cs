using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.Account;

public class ActionLogService(AppDatabase db)
{
    public async Task CreateActionLogAsync(
        Guid accountId,
        string action,
        Dictionary<string, object> meta,
        string? userAgent = null,
        string? ipAddress = null,
        Guid? sessionId = null)
    {
        var log = new SnActionLog
        {
            Action = action,
            AccountId = accountId,
            Meta = meta,
            UserAgent = userAgent,
            IpAddress = ipAddress,
            SessionId = sessionId
        };

        db.ActionLogs.Add(log);
        await db.SaveChangesAsync();
    }
}
