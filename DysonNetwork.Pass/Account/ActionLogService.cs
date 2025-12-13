using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Account;

public class ActionLogService(GeoService geo, FlushBufferService fbs)
{
    public void CreateActionLog(Guid accountId, string action, Dictionary<string, object> meta)
    {
        var log = new SnActionLog
        {
            Action = action,
            AccountId = accountId,
            Meta = meta,
        };

        fbs.Enqueue(log);
    }

    public void CreateActionLogFromRequest(string action, Dictionary<string, object> meta, HttpRequest request,
        SnAccount? account = null)
    {
        var log = new SnActionLog
        {
            Action = action,
            Meta = meta,
            UserAgent = request.Headers.UserAgent,
            IpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            Location = geo.GetPointFromIp(request.HttpContext.Connection.RemoteIpAddress?.ToString())
        };
        
        if (request.HttpContext.Items["CurrentUser"] is SnAccount currentUser)
            log.AccountId = currentUser.Id;
        else if (account != null)
            log.AccountId = account.Id;
        else
            throw new ArgumentException("No user context was found");
        
        if (request.HttpContext.Items["CurrentSession"] is SnAuthSession currentSession)
            log.SessionId = currentSession.Id;

        fbs.Enqueue(log);
    }
}
