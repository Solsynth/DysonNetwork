using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Padlock.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("/api/private/apps/{appId:guid}/notifications")]
public class CustomAppNotificationController(
    AppDatabase db,
    DyCustomAppService.DyCustomAppServiceClient customApps,
    DyRingService.DyRingServiceClient ring
) : ControllerBase
{
    private const string RequiredScope = "notifications.send";

    public record SendCustomAppNotificationRequest(
        Guid? AccountId,
        List<Guid>? AccountIds,
        bool BroadcastToAll,
        [MaxLength(1024)] string Topic,
        [MaxLength(1024)] string? Title,
        [MaxLength(1024)] string? Subtitle,
        [MaxLength(8192)] string? Body,
        string? ActionUri,
        string? PushType,
        bool IsSilent = false,
        bool IsSavable = true,
        Dictionary<string, object?>? Meta = null
    );

    [HttpPost]
    public async Task<IActionResult> Send(
        [FromRoute] Guid appId,
        [FromBody] SendCustomAppNotificationRequest request,
        CancellationToken ct
    )
    {
        var secret = ExtractBearerToken(Request.Headers.Authorization.ToString());
        if (string.IsNullOrWhiteSpace(secret))
            return Unauthorized("Missing app API key.");

        var secretCheck = await customApps.CheckCustomAppSecretAsync(new DyCheckCustomAppSecretRequest
        {
            AppId = appId.ToString(),
            Secret = secret,
            IsOidc = false,
        }, cancellationToken: ct);
        if (!secretCheck.Valid)
            return Unauthorized("Invalid app API key.");

        var appResponse = await customApps.GetCustomAppAsync(new DyGetCustomAppRequest
        {
            Id = appId.ToString()
        }, cancellationToken: ct);
        if (appResponse.App is null)
            return NotFound("App not found.");

        var appDeveloperResponse = await customApps.GetAppDeveloperAsync(new DyGetAppDeveloperRequest
        {
            AppId = appId.ToString()
        }, cancellationToken: ct);

        var requestedIds = new HashSet<Guid>();
        if (request.AccountId.HasValue)
            requestedIds.Add(request.AccountId.Value);
        if (request.AccountIds is { Count: > 0 })
            foreach (var id in request.AccountIds)
                requestedIds.Add(id);

        if (!request.BroadcastToAll && requestedIds.Count == 0)
            return BadRequest("Provide account_id, account_ids, or set broadcast_to_all=true.");

        var authorizedQuery = db.AuthorizedApps
            .AsNoTracking()
            .Where(x => x.AppId == appId)
            .Where(x => x.Type == AuthorizedAppType.Oidc)
            .Where(x => x.DeletedAt == null)
            .Where(x => x.Scopes.Contains(RequiredScope));

        if (!request.BroadcastToAll)
            authorizedQuery = authorizedQuery.Where(x => requestedIds.Contains(x.AccountId));

        var targetIds = await authorizedQuery
            .Select(x => x.AccountId.ToString())
            .Distinct()
            .ToListAsync(ct);

        if (targetIds.Count == 0)
            return Ok(new { sent = 0, scope = RequiredScope });

        var notification = new DyPushNotification
        {
            Topic = $"{appDeveloperResponse.Developer.PublisherName}.{appResponse.App.Slug}.{request.Topic}",
            Title = request.Title ?? string.Empty,
            Subtitle = request.Subtitle ?? string.Empty,
            Body = request.Body ?? string.Empty,
            ActionUri = request.ActionUri ?? string.Empty,
            PushType = request.PushType ?? string.Empty,
            IsSilent = request.IsSilent,
            IsSavable = request.IsSavable,
            AppId = appId.ToString(),
        };
        var meta = request.Meta is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(request.Meta);
        meta["sent_by_app"] = new Dictionary<string, object?>
        {
            ["id"] = appResponse.App.Id,
            ["slug"] = appResponse.App.Slug,
            ["name"] = appResponse.App.Name,
            ["publisher"] = appDeveloperResponse.Developer.PublisherName,
        };
        notification.Meta = ByteString.CopyFrom(JsonSerializer.SerializeToUtf8Bytes(meta));

        await ring.SendPushNotificationToUsersAsync(new DySendPushNotificationToUsersRequest
        {
            Notification = notification,
            UserIds = { targetIds }
        }, cancellationToken: ct);

        return Ok(new
        {
            sent = targetIds.Count,
            scope = RequiredScope,
            broadcast_to_all = request.BroadcastToAll,
        });
    }

    private static string? ExtractBearerToken(string? authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization))
            return null;
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = authorization["Bearer ".Length..].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
