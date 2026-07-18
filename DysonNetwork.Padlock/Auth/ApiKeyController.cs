using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Shared.Models;
using SharedAuth = DysonNetwork.Shared.Auth;
using NodaTime;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("/api/api-keys")]
[Authorize]
public class ApiKeyController(
    AuthService auth,
    AppDatabase db
) : ControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> ListApiKeys(CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var keys = await db.ApiKeys
            .Where(k => k.AccountId == user.Id)
            .Where(k => k.DeletedAt == null)
            .Include(snApiKey => snApiKey.Session)
            .ToListAsync(ct);

        return Ok(keys.Select(k => new { k.Id, k.Label, k.AppId, k.CreatedAt, k.Session.ExpiredAt }));
    }

    [HttpPost("")]
    [Authorize]
    [SharedAuth.AskPermission(SharedAuth.PermissionKeys.AuthApiKeysManage)]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var session = HttpContext.Items["CurrentSession"] as SnAuthSession;

        var key = await auth.CreateApiKey(user.Id, request.Label,
            request.ExpiredAt.HasValue
                ? SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromSeconds(request.ExpiredAt.Value.Ticks))
                : null, session);
        var token = await auth.IssueApiKeyToken(key);

        return Ok(new { key.Id, key.Label, key.AppId, token, key.CreatedAt, key.Session.ExpiredAt });
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    [SharedAuth.AskPermission(SharedAuth.PermissionKeys.AuthApiKeysManage)]
    public async Task<IActionResult> RevokeApiKey(Guid id, CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var key = await auth.GetApiKey(id, user.Id);
        if (key is null) return NotFound(new ApiError { Code = "PADLOCK_API_KEY_NOT_FOUND", Message = "API key not found.", Status = 404 });

        await auth.RevokeApiKeyToken(key);
        return Ok();
    }

    [HttpPost("{id:guid}/rotate")]
    [Authorize]
    [SharedAuth.AskPermission(SharedAuth.PermissionKeys.AuthApiKeysManage)]
    public async Task<IActionResult> RotateApiKey(Guid id, CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var key = await auth.GetApiKey(id, user.Id);
        if (key is null) return NotFound(new ApiError { Code = "PADLOCK_API_KEY_NOT_FOUND", Message = "API key not found.", Status = 404 });

        var rotated = await auth.RotateApiKeyToken(key);
        var token = await auth.IssueApiKeyToken(rotated);

        return Ok(new { token });
    }
}

public record CreateApiKeyRequest(string Label, DateTime? ExpiredAt);
