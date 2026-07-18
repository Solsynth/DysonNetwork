using DysonNetwork.Padlock.Models;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("/api/private/apps/{appId:guid}/accounts/{accountId:guid}/contacts")]
public class CustomAppContactController(
    AppDatabase db,
    DyCustomAppService.DyCustomAppServiceClient customApps
) : ControllerBase
{
    private const string RequiredScope = "contacts.read";

    [HttpGet]
    public async Task<IActionResult> ListContacts(
        [FromRoute] Guid appId,
        [FromRoute] Guid accountId,
        [FromQuery] AccountContactType? type,
        [FromQuery] bool verifiedOnly = false,
        CancellationToken ct = default)
    {
        var secret = ExtractApiKey(Request.Headers["X-Api-Key"].ToString());
        if (string.IsNullOrWhiteSpace(secret))
            return Unauthorized(new ApiError { Code = "APP_API_KEY_REQUIRED", Message = "Missing app API key.", Status = 401 });

        var secretCheck = await customApps.CheckCustomAppSecretAsync(new DyCheckCustomAppSecretRequest
        {
            AppId = appId.ToString(),
            Secret = secret,
            IsOidc = false,
        }, cancellationToken: ct);
        if (!secretCheck.Valid)
            return Unauthorized(new ApiError { Code = "APP_API_KEY_INVALID", Message = "Invalid app API key.", Status = 401 });

        var authorized = await db.AuthorizedApps
            .AsNoTracking()
            .Where(x => x.AppId == appId)
            .Where(x => x.AccountId == accountId)
            .Where(x => x.Type == AuthorizedAppType.Oidc)
            .Where(x => x.DeletedAt == null)
            .Where(x => x.Scopes.Contains(RequiredScope))
            .AnyAsync(ct);
        if (!authorized)
            return StatusCode(403, ApiError.Unauthorized($"Missing required scope: {RequiredScope}", forbidden: true));

        var query = db.AccountContacts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId);

        if (verifiedOnly)
            query = query.Where(c => c.VerifiedAt != null);

        if (type.HasValue)
            query = query.Where(c => c.Type == type.Value);

        var contacts = await query
            .OrderByDescending(c => c.IsPrimary)
            .ThenByDescending(c => c.VerifiedAt)
            .ToListAsync(ct);

        return Ok(contacts);
    }

    private static string? ExtractApiKey(string? apiKey)
    {
        var token = apiKey?.Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
