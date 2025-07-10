using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass;
using DysonNetwork.Pass.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Account;

[ApiController]
[Route("/api/notifications")]
public class NotificationController(AppDatabase db, NotificationService nty) : ControllerBase
{
    [HttpGet("count")]
    [Authorize]
    public async Task<ActionResult<int>> CountUnreadNotifications()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not Account currentUser) return Unauthorized();

        var count = await db.Notifications
            .Where(s => s.AccountId == currentUser.Id && s.ViewedAt == null)
            .CountAsync();
        return Ok(count);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Notification>>> ListNotifications(
        [FromQuery] int offset = 0,
        // The page size set to 5 is to avoid the client pulled the notification
        // but didn't render it in the screen-viewable region.
        [FromQuery] int take = 5
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not Account currentUser) return Unauthorized();

        var totalCount = await db.Notifications
            .Where(s => s.AccountId == currentUser.Id)
            .CountAsync();
        var notifications = await db.Notifications
            .Where(s => s.AccountId == currentUser.Id)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        await nty.MarkNotificationsViewed(notifications);

        return Ok(notifications);
    }

    public class PushNotificationSubscribeRequest
    {
        [MaxLength(4096)] public string DeviceToken { get; set; } = null!;
        public NotificationPushProvider Provider { get; set; }
    }

    [HttpPut("subscription")]
    [Authorize]
    public async Task<ActionResult<NotificationPushSubscription>> SubscribeToPushNotification(
        [FromBody] PushNotificationSubscribeRequest request
    )
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        if (currentUser == null) return Unauthorized();
        var currentSession = currentSessionValue as Session;
        if (currentSession == null) return Unauthorized();

        var result =
            await nty.SubscribePushNotification(currentUser, request.Provider, currentSession.Challenge.DeviceId!,
                request.DeviceToken);

        return Ok(result);
    }

    [HttpDelete("subscription")]
    [Authorize]
    public async Task<ActionResult<int>> UnsubscribeFromPushNotification()
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        if (currentUser == null) return Unauthorized();
        var currentSession = currentSessionValue as Session;
        if (currentSession == null) return Unauthorized();

        var affectedRows = await db.NotificationPushSubscriptions
            .Where(s =>
                s.AccountId == currentUser.Id &&
                s.DeviceId == currentSession.Challenge.DeviceId
            ).ExecuteDeleteAsync();
        return Ok(affectedRows);
    }

    public class NotificationRequest
    {
        [Required] [MaxLength(1024)] public string Topic { get; set; } = null!;
        [Required] [MaxLength(1024)] public string Title { get; set; } = null!;
        [MaxLength(2048)] public string? Subtitle { get; set; }
        [Required] [MaxLength(4096)] public string Content { get; set; } = null!;
        public Dictionary<string, object>? Meta { get; set; }
        public int Priority { get; set; } = 10;
    }

    [HttpPost("broadcast")]
    [Authorize]
    [RequiredPermission("global", "notifications.broadcast")]
    public async Task<ActionResult> BroadcastNotification(
        [FromBody] NotificationRequest request,
        [FromQuery] bool save = false
    )
    {
        await nty.BroadcastNotification(
            new Notification
            {
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                UpdatedAt = SystemClock.Instance.GetCurrentInstant(),
                Topic = request.Topic,
                Title = request.Title,
                Subtitle = request.Subtitle,
                Content = request.Content,
                Meta = request.Meta,
                Priority = request.Priority,
            },
            save
        );
        return Ok();
    }

    public class NotificationWithAimRequest : NotificationRequest
    {
        [Required] public List<Guid> AccountId { get; set; } = null!;
    }
    
    [HttpPost("send")]
    [Authorize]
    [RequiredPermission("global", "notifications.send")]
    public async Task<ActionResult> SendNotification(
        [FromBody] NotificationWithAimRequest request,
        [FromQuery] bool save = false
    )
    {
        var accounts = await db.Accounts.Where(a => request.AccountId.Contains(a.Id)).ToListAsync();
        await nty.SendNotificationBatch(
            new Notification
            {
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                UpdatedAt = SystemClock.Instance.GetCurrentInstant(),
                Topic = request.Topic,
                Title = request.Title,
                Subtitle = request.Subtitle,
                Content = request.Content,
                Meta = request.Meta,
            },
            accounts,
            save
        );
        return Ok();
    }
}