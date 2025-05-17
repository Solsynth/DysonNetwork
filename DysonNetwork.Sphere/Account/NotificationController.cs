using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Post;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("/notifications")]
public class NotificationController(AppDatabase db, NotificationService nty) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Notification>>> ListNotifications([FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        if (currentUser == null) return Unauthorized();

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
}