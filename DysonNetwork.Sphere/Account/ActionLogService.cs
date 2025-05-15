using Quartz;
using System.Collections.Concurrent;
using DysonNetwork.Sphere.Connection;
using Microsoft.AspNetCore.Http;

namespace DysonNetwork.Sphere.Account;

public class ActionLogService(AppDatabase db, GeoIpService geo) : IDisposable
{
    private readonly ConcurrentQueue<ActionLog> _creationQueue = new();

    public void CreateActionLog(Guid accountId, string action, Dictionary<string, object> meta)
    {
        var log = new ActionLog
        {
            Action = action,
            AccountId = accountId,
            Meta = meta,
        };

        _creationQueue.Enqueue(log);
    }
    
    public void CreateActionLogFromRequest(string action, Dictionary<string, object> meta, HttpRequest request)
    {
        if (request.HttpContext.Items["CurrentUser"] is not Account currentUser)
            throw new ArgumentException("No user context was found");
        if (request.HttpContext.Items["CurrentSession"] is not Auth.Session currentSession)
            throw new ArgumentException("No session context was found");
        
        var log = new ActionLog
        {
            Action = action,
            AccountId = currentUser.Id,
            SessionId = currentSession.Id,
            Meta = meta,
            UserAgent = request.Headers.UserAgent,
            IpAddress = request.HttpContext.Connection.RemoteIpAddress?.ToString(),
            Location = geo.GetPointFromIp(request.HttpContext.Connection.RemoteIpAddress?.ToString())
        };
    
        _creationQueue.Enqueue(log);
    }
    
    public async Task FlushQueue()
    {
        var workingQueue = new List<ActionLog>();
        while (_creationQueue.TryDequeue(out var log))
            workingQueue.Add(log);

        if (workingQueue.Count != 0)
        {
            try
            {
                await db.ActionLogs.AddRangeAsync(workingQueue);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                foreach (var log in workingQueue)
                    _creationQueue.Enqueue(log);
                throw;
            }
        }
    }

    public void Dispose()
    {
        FlushQueue().Wait();
        GC.SuppressFinalize(this);
    }
}

public class ActionLogFlushJob(ActionLogService als) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await als.FlushQueue();
    }
}