using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
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
    PushService nty,
    NotificationPreferenceService preferenceService
) : ControllerBase
{
    [HttpGet("count")]
    [Authorize]
    public async Task<ActionResult<int>> CountUnreadNotifications([FromQuery] string? app = null)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);
        var count = await nty.ApplyNotificationAppFilter(
                db.Notifications.Where(s => s.AccountId == accountId && s.ViewedAt == null),
                app,
                useDefaultIfMissing: true
            )
            .CountAsync();
        return Ok(count);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnNotification>>> ListNotifications(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 8,
        [FromQuery] bool unmark = false,
        [FromQuery] string? app = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);
        var baseQuery = nty.ApplyNotificationAppFilter(
            db.Notifications.Where(s => s.AccountId == accountId),
            app,
            useDefaultIfMissing: true
        );

        var totalCount = await baseQuery
            .CountAsync();
        var notifications = await baseQuery
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
    [AskPermission(PermissionKeys.NotificationsReadAll)]
    public async Task<ActionResult> MarkAllNotificationsViewed([FromQuery] string? app = null)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        await nty.MarkAllNotificationsViewed(accountId, app);
        return Ok();
    }

    public class PushNotificationSubscribeRequest
    {
        [MaxLength(4096)] public string? DeviceToken { get; set; }
        [MaxLength(4096)] public string? DeviceName { get; set; }
        public PushProvider Provider { get; set; }
        [MaxLength(1024)] public string? AppId { get; set; }
    }

    [HttpPut("subscription")]
    [Authorize]
    [AskPermission(PermissionKeys.NotificationsSubscriptionsManage)]
    public async Task<ActionResult<SnNotificationPushSubscription>> SubscribeToPushNotification(
        [FromBody] PushNotificationSubscribeRequest request,
        [FromQuery] bool force = false
    )
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser || currentSessionValue is not DyAuthSession currentSession)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        if (request.Provider == PushProvider.Sop)
            return BadRequest(new ApiError { Code = "RING_NOTIFICATION_SOP_NOT_SUPPORTED", Message = "Use /api/notifications/sop/subscription to register SOP provider.", Status = 400 });
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
            return BadRequest(new ApiError { Code = "RING_NOTIFICATION_DEVICE_TOKEN_REQUIRED", Message = "DeviceToken is required.", Status = 400 });
        if (request.Provider == PushProvider.UnifiedPush && !IsValidUnifiedPushEndpoint(request.DeviceToken))
            return BadRequest(new ApiError { Code = "RING_NOTIFICATION_INVALID_UP_ENDPOINT", Message = "For UnifiedPush, DeviceToken must be a valid absolute HTTP(S) endpoint URL.", Status = 400 });

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
                request.DeviceName,
                request.Provider,
                currentUser,
                appId: request.AppId
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
    public async Task<ActionResult<List<SnNotificationPushSubscription>>> ListPushSubscriptions(
        [FromQuery] string? app = null
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);
        var query = nty.ApplySubscriptionAppFilter(
            db.PushSubscriptions.Where(s => s.AccountId == accountId),
            app,
            useDefaultIfMissing: true
        );

        var subscriptions = await query
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
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });

        var accountId = Guid.Parse(currentUser.Id);
        var subscription = await nty.GetCurrentDeviceActiveSubscription(accountId, currentSession.ClientId);
        return Ok(subscription);
    }

    [HttpDelete("subscription/{subscriptionId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.NotificationsSubscriptionsManage)]
    public async Task<ActionResult<int>> UnsubscribeFromPushNotification(Guid subscriptionId)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
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
        [MaxLength(64)] public string? PushType { get; set; }
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
        [FromQuery] bool save = false,
        [FromQuery] string? app = null
    )
    {
        var appId = nty.ResolveAppId(app, useDefaultIfMissing: true);
        var notification = new SnNotification
        {
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UpdatedAt = SystemClock.Instance.GetCurrentInstant(),
            Topic = request.Topic,
            Title = request.Title,
            Subtitle = request.Subtitle,
            Content = request.Content,
            AppId = appId,
            PushType = request.PushType
        };
        if (request.Meta != null)
        {
            notification.Meta = request.Meta;
        }

        await nty.SendNotificationBatch(notification, request.AccountId, save);
        return Ok();
    }

    [HttpGet("preferences")]
    [Authorize]
    public async Task<ActionResult<List<SnNotificationPreference>>> ListPreferences([FromQuery] string? app = null)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var preferences = await preferenceService.GetPreferencesAsync(accountId);
        return Ok(preferences);
    }

    [HttpGet("preferences/{topic}")]
    [Authorize]
    public async Task<ActionResult<NotificationPreferenceLevel>> GetPreference(string topic)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        var preference = await preferenceService.GetPreferenceAsync(accountId, topic);
        return Ok(preference);
    }

    public class SetPreferenceRequest
    {
        [Required] public NotificationPreferenceLevel Preference { get; set; }
    }

    [HttpPut("preferences/{topic}")]
    [Authorize]
    [AskPermission(PermissionKeys.NotificationsPreferencesManage)]
    public async Task<ActionResult> SetPreference(string topic, [FromBody] SetPreferenceRequest request)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        await preferenceService.SetPreferenceAsync(accountId, topic, request.Preference);
        return Ok();
    }

    [HttpDelete("preferences/{topic}")]
    [Authorize]
    [AskPermission(PermissionKeys.NotificationsPreferencesManage)]
    public async Task<ActionResult> DeletePreference(string topic)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });
        var accountId = Guid.Parse(currentUser.Id);

        await preferenceService.DeletePreferenceAsync(accountId, topic);
        return Ok();
    }
}
