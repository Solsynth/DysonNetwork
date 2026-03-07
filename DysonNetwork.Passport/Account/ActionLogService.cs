using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Passport.Account;

public class ActionLogService(RemoteActionLogService remoteActionLogs)
{
    public void CreateActionLog(Guid accountId, string action, Dictionary<string, object> meta)
    {
        remoteActionLogs.CreateActionLog(accountId, action, meta);
    }

    public void CreateActionLogFromRequest(string action, Dictionary<string, object> meta, HttpRequest request,
        SnAccount? account = null)
    {
        Guid accountId;
        if (request.HttpContext.Items["CurrentUser"] is SnAccount currentUser)
            accountId = currentUser.Id;
        else if (request.HttpContext.Items["CurrentUser"] is DyAccount protoAccount &&
                 Guid.TryParse(protoAccount.Id, out var protoAccountId))
            accountId = protoAccountId;
        else if (account is not null)
            accountId = account.Id;
        else
            throw new ArgumentException("No user context was found");

        Guid? sessionId = null;
        if (request.HttpContext.Items["CurrentSession"] is SnAuthSession currentSession)
            sessionId = currentSession.Id;
        else if (request.HttpContext.Items["CurrentSession"] is DyAuthSession protoSession &&
                 Guid.TryParse(protoSession.Id, out var protoSessionId))
            sessionId = protoSessionId;

        var ipAddress = request.GetClientIpAddress();
        remoteActionLogs.CreateActionLog(
            accountId,
            action,
            meta,
            request.Headers.UserAgent,
            ipAddress,
            null,
            sessionId);
    }
}
