using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("api/v1/auth/apikey")]
[Authorize]
public class ApiKeyController(
    AuthService auth,
    AppDatabase db,
    IHttpContextAccessor httpContextAccessor
) : ControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> ListApiKeys(CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized();

        var keys = await db.ApiKeys
            .Where(k => k.AccountId == user.Id)
            .Where(k => k.DeletedAt == null)
            .ToListAsync(ct);

        return Ok(keys.Select(k => new { k.Id, k.Label, k.CreatedAt, k.Session.ExpiredAt }));
    }

    [HttpPost("")]
    public async Task<IActionResult> CreateApiKey([FromBody] CreateApiKeyRequest request, CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized();

        var session = HttpContext.Items["CurrentSession"] as SnAuthSession;
        
        var key = await auth.CreateApiKey(user.Id, request.Label, request.ExpiredAt.HasValue ? SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromSeconds(request.ExpiredAt.Value.Ticks)) : null, session);
        var token = await auth.IssueApiKeyToken(key);

        return Ok(new { key.Id, key.Label, token, key.CreatedAt, key.Session.ExpiredAt });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RevokeApiKey(Guid id, CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized();

        var key = await auth.GetApiKey(id, user.Id);
        if (key is null) return NotFound();

        await auth.RevokeApiKeyToken(key);
        return Ok();
    }

    [HttpPost("{id:guid}/rotate")]
    public async Task<IActionResult> RotateApiKey(Guid id, CancellationToken ct)
    {
        var user = HttpContext.Items["CurrentUser"] as SnAccount;
        if (user is null) return Unauthorized();

        var key = await auth.GetApiKey(id, user.Id);
        if (key is null) return NotFound();

        var rotated = await auth.RotateApiKeyToken(key);
        var token = await auth.IssueApiKeyToken(rotated);

        return Ok(new { token });
    }
}

public record CreateApiKeyRequest(string Label, DateTime? ExpiredAt);
