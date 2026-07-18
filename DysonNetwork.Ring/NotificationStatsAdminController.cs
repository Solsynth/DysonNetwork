using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring;

[ApiController]
[Route("/api/admin/stats")]
[Authorize]
[ApiFeature("admin.stats", Revision = 1)]
public class NotificationStatsAdminController(AppDatabase db, IClock clock) : ControllerBase
{
    public class NotificationStatsResponse
    {
        public Instant CalculatedAt { get; set; }
        public long TotalNotifications { get; set; }
        public long UnreadNotifications { get; set; }
        public long NotificationsLastDay { get; set; }
        public long NotificationsLastWeek { get; set; }
        public long NotificationsLastMonth { get; set; }
        public long TotalPushSubscriptions { get; set; }
        public long ActivePushSubscriptions { get; set; }
        public long TotalSendRequests { get; set; }
        public long TotalDeliveryAttempts { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.NotificationsSend)]
    public async Task<ActionResult<NotificationStatsResponse>> GetStats(CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        var oneDayAgo = now - Duration.FromDays(1);
        var sevenDaysAgo = now - Duration.FromDays(7);
        var thirtyDaysAgo = now - Duration.FromDays(30);
        var notifications = db.Notifications.AsNoTracking();

        return Ok(new NotificationStatsResponse
        {
            CalculatedAt = now,
            TotalNotifications = await notifications.LongCountAsync(cancellationToken),
            UnreadNotifications = await notifications.LongCountAsync(n => n.ViewedAt == null, cancellationToken),
            NotificationsLastDay = await notifications.LongCountAsync(n => n.CreatedAt >= oneDayAgo, cancellationToken),
            NotificationsLastWeek = await notifications.LongCountAsync(n => n.CreatedAt >= sevenDaysAgo, cancellationToken),
            NotificationsLastMonth = await notifications.LongCountAsync(n => n.CreatedAt >= thirtyDaysAgo, cancellationToken),
            TotalPushSubscriptions = await db.PushSubscriptions.AsNoTracking().LongCountAsync(cancellationToken),
            ActivePushSubscriptions = await db.PushSubscriptions.AsNoTracking().LongCountAsync(s => s.IsActivated, cancellationToken),
            TotalSendRequests = await db.NotificationSendRecords.AsNoTracking().LongCountAsync(cancellationToken),
            TotalDeliveryAttempts = await db.NotificationDeliveryRecords.AsNoTracking().LongCountAsync(cancellationToken)
        });
    }
}
