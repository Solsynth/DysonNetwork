using Quartz;
using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Storage;
using DysonNetwork.Sphere.Storage.Handlers;

namespace DysonNetwork.Sphere.Account;

public class ActionLogService(GeoIpService geo, FlushBufferService fbs)
{
    public void CreateActionLog(Guid accountId, string action, Dictionary<string, object> meta)
    {
        var log = new ActionLog
        {
            Action = action,
            AccountId = accountId,
            Meta = meta,
        };

        fbs.Enqueue(log);
    }

    public void CreateActionLogFromRequest(string action, Dictionary<string, object> meta, HttpRequest request,
        Account? account = null)
    {
        var log = new ActionLog
        {
            Action = action,
            Meta = meta,
            UserAgent = request.Headers.UserAgent,
            IpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            Location = geo.GetPointFromIp(request.HttpContext.Connection.RemoteIpAddress?.ToString())
        };
        
        if (request.HttpContext.Items["CurrentUser"] is Account currentUser)
            log.AccountId = currentUser.Id;
        else if (account != null)
            log.AccountId = account.Id;
        else
            throw new ArgumentException("No user context was found");
        
        if (request.HttpContext.Items["CurrentSession"] is Auth.Session currentSession)
            log.SessionId = currentSession.Id;

        fbs.Enqueue(log);
    }
}