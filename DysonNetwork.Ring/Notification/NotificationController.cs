using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Net;

namespace DysonNetwork.Ring.Notification;

[ApiController]
[Route("/api/notifications")]
public class NotificationController(
    AppDatabase db,
    PushService nty
) : ControllerBase
{
    [HttpGet("count")]
    [Authorize]
    public async Task<ActionResult<int>> CountUnreadNotifications()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var count = await db.Notifications
            .Where(s => s.AccountId == accountId && s.ViewedAt == null)
            .CountAsync();
        return Ok(count);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnNotification>>> ListNotifications(
        [FromQuery] int offset = 0,
        // The page size set to 5 is to avoid the client pulled the notification
        // but didn't render it in the screen-viewable region.
        [FromQuery] int take = 8,
        [FromQuery] bool unmark = false
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var totalCount = await db.Notifications
            .Where(s => s.AccountId == accountId)
            .CountAsync();
        var notifications = await db.Notifications
            .Where(s => s.AccountId == accountId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        if (!unmark) await nty.MarkNotificationsViewed(notifications.ToList());

        return Ok(notifications);
    }

    [HttpPost("all/read")]
    [Authorize]
    public async Task<ActionResult> MarkAllNotificationsViewed()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        await nty.MarkAllNotificationsViewed(accountId);
        return Ok();
    }

    public class PushNotificationSubscribeRequest
    {
        [MaxLength(4096)] public string? DeviceToken { get; set; }
        public PushProvider Provider { get; set; }
    }

    [HttpPut("subscription")]
    [Authorize]
    public async Task<ActionResult<SnNotificationPushSubscription>> SubscribeToPushNotification(
        [FromBody] PushNotificationSubscribeRequest request,
        [FromQuery] bool force = false
    )
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser || currentSessionValue is not DyAuthSession currentSession)
            return Unauthorized();
        if (request.Provider == PushProvider.Sop)
            return BadRequest("Use /api/notifications/sop/subscription to register SOP provider.");
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
            return BadRequest("DeviceToken is required.");
        if (request.Provider == PushProvider.UnifiedPush && !IsValidUnifiedPushEndpoint(request.DeviceToken))
            return BadRequest("For UnifiedPush, DeviceToken must be a valid absolute HTTP(S) endpoint URL.");

        var accountId = Guid.Parse(currentUser.Id);
        if (!force)
        {
            var activeSubscription = await nty.GetCurrentDeviceActiveSubscription(accountId, currentSession.ClientId);
            if (activeSubscription?.Provider == PushProvider.Sop)
                return Ok(activeSubscription);
        }

        var result =
            await nty.SubscribeDevice(
                currentSession.ClientId,
                request.DeviceToken,
                request.Provider,
                currentUser
            );

        return Ok(result);
    }

    private static bool IsValidUnifiedPushEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return false;
        if (uri.Scheme is not ("http" or "https"))
            return false;

        if (IPAddress.TryParse(uri.Host, out var ipAddress))
            return !IPAddress.IsLoopback(ipAddress);

        return !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    [HttpGet("subscription")]
    [Authorize]
    public async Task<ActionResult<List<SnNotificationPushSubscription>>> ListPushSubscriptions()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var subscriptions = await db.PushSubscriptions
            .Where(s => s.AccountId == accountId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();

        return Ok(subscriptions);
    }

    [HttpGet("subscription/current")]
    [Authorize]
    public async Task<ActionResult<SnNotificationPushSubscription?>> GetCurrentDeviceActiveSubscription()
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser || currentSessionValue is not DyAuthSession currentSession)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var subscription = await nty.GetCurrentDeviceActiveSubscription(accountId, currentSession.ClientId);
        return Ok(subscription);
    }

    [HttpDelete("subscription/{subscriptionId:guid}")]
    [Authorize]
    public async Task<ActionResult<int>> UnsubscribeFromPushNotification(Guid subscriptionId)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var affectedRows = await db.PushSubscriptions
            .Where(s => s.AccountId == accountId && s.Id == subscriptionId)
            .ExecuteDeleteAsync();
        return Ok(affectedRows);
    }

    public class NotificationRequest
    {
        [Required] [MaxLength(1024)] public string Topic { get; set; } = null!;
        [Required] [MaxLength(1024)] public string Title { get; set; } = null!;
        [MaxLength(2048)] public string? Subtitle { get; set; }
        [Required] [MaxLength(4096)] public string Content { get; set; } = null!;
        public Dictionary<string, object?>? Meta { get; set; }
        public int Priority { get; set; } = 10;
    }

    public class NotificationWithAimRequest : NotificationRequest
    {
        [Required] public List<Guid> AccountId { get; set; } = null!;
    }

    [HttpPost("send")]
    [Authorize]
    [AskPermission("notifications.send")]
    public async Task<ActionResult> SendNotification(
        [FromBody] NotificationWithAimRequest request,
        [FromQuery] bool save = false
    )
    {
        var notification = new SnNotification
        {
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UpdatedAt = SystemClock.Instance.GetCurrentInstant(),
            Topic = request.Topic,
            Title = request.Title,
            Subtitle = request.Subtitle,
            Content = request.Content,
        };
        if (request.Meta != null)
        {
            notification.Meta = request.Meta;
        }

        await nty.SendNotificationBatch(notification, request.AccountId, save);
        return Ok();
    }
}
