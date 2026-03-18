using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Queue;
using System.Text.Json;

namespace DysonNetwork.Padlock.Account;

public class ActionLogService(AppDatabase db, IEventBus eventBus, GeoService geoService)
{
    public async Task<SnActionLog> CreateActionLogAsync(
        Guid accountId,
        string action,
        Dictionary<string, object> meta,
        string? userAgent = null,
        string? ipAddress = null,
        string? location = null,
        Guid? sessionId = null)
    {
        GeoPoint? parsedLocation = null;
        if (!string.IsNullOrWhiteSpace(location))
        {
            try
            {
                parsedLocation = JsonSerializer.Deserialize<GeoPoint>(location);
            }
            catch (JsonException)
            {
            }
        }

        var log = new SnActionLog
        {
            Action = action,
            AccountId = accountId,
            Meta = meta,
            UserAgent = userAgent,
            IpAddress = ipAddress,
            Location = parsedLocation ?? geoService.GetPointFromIp(ipAddress),
            SessionId = sessionId
        };

        db.ActionLogs.Add(log);
        await db.SaveChangesAsync();

        if (ProgressionActionLogRegistry.ShouldPublish(action))
        {
            await eventBus.PublishAsync(
                ActionLogTriggeredEvent.GetSubject(action),
                new ActionLogTriggeredEvent
                {
                    ActionLogId = log.Id,
                    AccountId = accountId,
                    Action = action,
                    Meta = meta,
                    SessionId = sessionId,
                    OccurredAt = log.CreatedAt
                }
            );
        }

        return log;
    }
}
