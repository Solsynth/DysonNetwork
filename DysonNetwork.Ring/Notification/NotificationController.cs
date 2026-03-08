using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring.Notification;

[ApiController]
[Route("/api/notifications")]
public class NotificationController(
    AppDatabase db,
    PushService nty,
    IOptions<JsonOptions> jsonOptions
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
    public async Task<ActionResult<SnNotificationPushSubscription>>
        SubscribeToPushNotification(
            [FromBody] PushNotificationSubscribeRequest request
        )
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser || currentSessionValue is not DyAuthSession currentSession)
            return Unauthorized();
        if (request.Provider == PushProvider.Sop)
            return BadRequest("Use /api/notifications/subscription/sop to register SOP provider.");
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
            return BadRequest("DeviceToken is required.");

        var result =
            await nty.SubscribeDevice(
                currentSession.ClientId,
                request.DeviceToken,
                request.Provider,
                currentUser
            );

        return Ok(result);
    }

    public class SopRegistrationResponse
    {
        public string Token { get; set; } = null!;
        public SnNotificationPushSubscription Subscription { get; set; } = null!;
    }

    [HttpPost("subscription/sop")]
    [Authorize]
    public async Task<ActionResult<SopRegistrationResponse>> RegisterSopToken()
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser || currentSessionValue is not DyAuthSession currentSession)
            return Unauthorized();

        var sopDeviceId = $"{currentSession.ClientId}:sop";
        var (token, subscription) = await nty.RegisterSopToken(sopDeviceId, currentUser);
        return Ok(new SopRegistrationResponse
        {
            Token = token,
            Subscription = subscription
        });
    }

    [HttpGet("sop")]
    [AllowAnonymous]
    public async Task<ActionResult<List<SnNotification>>> ListNotificationsBySopToken(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 8
    )
    {
        var token = ExtractSopToken(Request);
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();

        var sopSub = await nty.GetSopSubscriptionByToken(token);
        if (sopSub is null) return Unauthorized();

        var totalCount = await db.Notifications
            .Where(s => s.AccountId == sopSub.AccountId)
            .CountAsync();
        var notifications = await db.Notifications
            .Where(s => s.AccountId == sopSub.AccountId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(notifications);
    }

    [HttpGet("sop/stream")]
    [AllowAnonymous]
    public async Task<ActionResult> StreamNotificationsBySopToken()
    {
        var token = ExtractSopToken(Request);
        if (string.IsNullOrWhiteSpace(token)) return Unauthorized();

        var sopSub = await nty.GetSopSubscriptionByToken(token);
        if (sopSub is null) return Unauthorized();

        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var (streamId, reader) = nty.SubscribeSopStream(sopSub.AccountId);
        try
        {
            await Response.WriteAsync("event: ready\n");
            await Response.WriteAsync("data: {\"status\":\"connected\"}\n\n");
            await Response.Body.FlushAsync();

            var jsonSerializerOptions = jsonOptions.Value.JsonSerializerOptions;
            while (await reader.WaitToReadAsync(HttpContext.RequestAborted))
            {
                while (reader.TryRead(out var notification))
                {
                    var payload = JsonSerializer.Serialize(notification, jsonSerializerOptions);
                    await Response.WriteAsync("event: notification\n");
                    await Response.WriteAsync($"data: {payload}\n\n");
                    await Response.Body.FlushAsync();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected
        }
        finally
        {
            nty.UnsubscribeSopStream(sopSub.AccountId, streamId);
        }

        return new EmptyResult();
    }

    [HttpDelete("subscription")]
    [Authorize]
    public async Task<ActionResult<int>> UnsubscribeFromPushNotification()
    {
        HttpContext.Items.TryGetValue("CurrentSession", out var currentSessionValue);
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        if (currentUserValue is not DyAccount currentUser || currentSessionValue is not DyAuthSession currentSession)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var affectedRows = await db.PushSubscriptions
            .Where(s =>
                s.AccountId == accountId &&
                s.DeviceId == currentSession.ClientId
            ).ExecuteDeleteAsync();
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

    private static string? ExtractSopToken(HttpRequest request)
    {
        var queryToken = request.Query["token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(queryToken)) return queryToken;

        var sopHeader = request.Headers["X-SOP-Token"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(sopHeader)) return sopHeader;

        var authHeader = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader)) return null;
        const string sopPrefix = "SOP ";
        return authHeader.StartsWith(sopPrefix, StringComparison.OrdinalIgnoreCase)
            ? authHeader[sopPrefix.Length..].Trim()
            : null;
    }
}
