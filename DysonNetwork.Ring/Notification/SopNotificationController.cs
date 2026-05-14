using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DysonNetwork.Ring.Notification;

[ApiController]
[Route("/api/notifications/sop")]
public class SopNotificationController(
    AppDatabase db,
    PushService nty,
    IOptions<JsonOptions> jsonOptions
) : ControllerBase
{
    public class SopRegistrationResponse
    {
        public string Token { get; set; } = null!;
        public SnNotificationPushSubscription Subscription { get; set; } = null!;
    }

    [HttpPost("subscription")]
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

    [HttpGet]
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

    [HttpGet("stream")]
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

        var (streamId, reader) = nty.SubscribeSopStream(sopSub.AccountId, sopSub.DeviceId);
        try
        {
            await Response.WriteAsync("event: ready\n");
            await Response.WriteAsync("data: {\"status\":\"connected\"}\n\n");
            await Response.Body.FlushAsync();

            var serializerOptions = jsonOptions.Value.JsonSerializerOptions;
            while (await reader.WaitToReadAsync(HttpContext.RequestAborted))
            {
                while (reader.TryRead(out var notification))
                {
                    var payload = JsonSerializer.Serialize(notification, serializerOptions);
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
