using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using AccountService = DysonNetwork.Shared.Proto.AccountService;

namespace DysonNetwork.Pusher.Notification;

[ApiController]
[Route("/api/notifications")]
public class NotificationController(
    AppDatabase db,
    PushService nty,
    AccountService.AccountServiceClient accounts) : ControllerBase
{
    [HttpGet("count")]
    [Authorize]
    public async Task<ActionResult<int>> CountUnreadNotifications()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var count = await db.Notifications
            .Where(s => s.AccountId == accountId && s.ViewedAt == null)
            .CountAsync();
        return Ok(count);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Notification>>> ListNotifications(
        [FromQuery] int offset = 0,
        // The page size set to 5 is to avoid the client pulled the notification
        // but didn't render it in the screen-viewable region.
        [FromQuery] int take = 8
    )
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not Account currentUser) return Unauthorized();
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
        await nty.MarkNotificationsViewed(notifications.ToList());

        return Ok(notifications);
    }

    public class PushNotificationSubscribeRequest
    {
        [MaxLength(4096)] public string DeviceToken { get; set; } = null!;
        public PushProvider Provider { get; set; }
    }

    [HttpPut("subscription")]
    [Authorize]
    public async Task<ActionResult<PushSubscription>>
        SubscribeToPushNotification(
            [FromBody] PushNotificationSubscribeRequest request
        )
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        if (currentUser == null) return Unauthorized();
        var currentSession = currentSessionValue as AuthSession;
        if (currentSession == null) return Unauthorized();

        var result =
            await nty.SubscribeDevice(
                currentSession.Challenge.DeviceId!,
                request.DeviceToken,
                request.Provider,
                currentUser
            );

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
        var currentSession = currentSessionValue as AuthSession;
        if (currentSession == null) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var affectedRows = await db.PushSubscriptions
            .Where(s =>
                s.AccountId == accountId &&
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
            request.AccountId,
            save
        );
        return Ok();
    }
}