using DysonNetwork.Pass.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Cache;
using NodaTime;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Auth.OpenId;

[ApiController]
[Route("/api/accounts/me/connections")]
[Authorize]
public class ConnectionController(
    AppDatabase db,
    IEnumerable<OidcService> oidcServices,
    AccountService accounts,
    AuthService auth,
    ICacheService cache
) : ControllerBase
{
    private const string StateCachePrefix = "oidc-state:";
    private const string ReturnUrlCachePrefix = "oidc-returning:";
    private static readonly TimeSpan StateExpiration = TimeSpan.FromMinutes(15);

    [HttpGet]
    public async Task<ActionResult<List<SnAccountConnection>>> GetConnections()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var connections = await db.AccountConnections
            .Where(c => c.AccountId == currentUser.Id)
            .Select(c => new
            {
                c.Id,
                c.AccountId,
                c.Provider,
                c.ProvidedIdentifier,
                c.Meta,
                c.LastUsedAt,
                c.CreatedAt,
                c.UpdatedAt,
            })
            .ToListAsync();
        return Ok(connections);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> RemoveConnection(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var connection = await db.AccountConnections
            .Where(c => c.Id == id && c.AccountId == currentUser.Id)
            .FirstOrDefaultAsync();
        if (connection == null)
            return NotFound();

        db.AccountConnections.Remove(connection);
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("/api/auth/connect/apple/mobile")]
    public async Task<ActionResult> ConnectAppleMobile([FromBody] AppleMobileConnectRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        if (GetOidcService("apple") is not AppleOidcService appleService)
            return StatusCode(503, "Apple OIDC service not available");

        var callbackData = new OidcCallbackData
        {
            IdToken = request.IdentityToken,
            Code = request.AuthorizationCode,
        };

        OidcUserInfo userInfo;
        try
        {
            userInfo = await appleService.ProcessCallbackAsync(callbackData);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error processing Apple token: {ex.Message}");
        }

        var existingConnection = await db.AccountConnections
            .FirstOrDefaultAsync(c =>
                c.Provider == "apple" &&
                c.ProvidedIdentifier == userInfo.UserId);

        if (existingConnection != null)
        {
            return BadRequest(
                $"This Apple account is already linked to {(existingConnection.AccountId == currentUser.Id ? "your account" : "another user")}.");
        }

        db.AccountConnections.Add(new SnAccountConnection
        {
            AccountId = currentUser.Id,
            Provider = "apple",
            ProvidedIdentifier = userInfo.UserId!,
            AccessToken = userInfo.AccessToken,
            RefreshToken = userInfo.RefreshToken,
            LastUsedAt = SystemClock.Instance.GetCurrentInstant(),
            Meta = userInfo.ToMetadata(),
        });

        await db.SaveChangesAsync();

        return Ok(new { message = "Successfully connected Apple account." });
    }

    private OidcService? GetOidcService(string provider)
    {
        return oidcServices.FirstOrDefault(s => s.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
    }

    public class ConnectProviderRequest
    {
        public string Provider { get; set; } = null!;
        public string? ReturnUrl { get; set; }
    }

    [AllowAnonymous]
    [Route("/auth/callback/{provider}")]
    [HttpGet, HttpPost]
    public async Task<IActionResult> HandleCallback([FromRoute] string provider)
    {
        var oidcService = GetOidcService(provider);
        if (oidcService == null)
            return BadRequest($"Provider '{provider}' is not supported.");

        var callbackData = await ExtractCallbackData(Request);
        if (callbackData.State == null)
            return BadRequest("State parameter is missing.");

        // Get the state from the cache
        var stateKey = $"{StateCachePrefix}{callbackData.State}";
        
        // Try to get the state as OidcState first (new format)
        var oidcState = await cache.GetAsync<OidcState>(stateKey);
        
        // If not found, try to get as string (legacy format)
        if (oidcState == null)
        {
            var stateValue = await cache.GetAsync<string>(stateKey);
            if (string.IsNullOrEmpty(stateValue) || !OidcState.TryParse(stateValue, out oidcState) || oidcState == null)
                return BadRequest("Invalid or expired state parameter");
        }
        
        // Remove the state from cache to prevent replay attacks
        await cache.RemoveAsync(stateKey);

        // Handle the flow based on state type
        if (oidcState is { FlowType: OidcFlowType.Connect, AccountId: not null })
        {
            // Connection flow
            if (oidcState.DeviceId != null)
            {
                callbackData.State = oidcState.DeviceId;
            }
            return await HandleManualConnection(provider, oidcService, callbackData, oidcState.AccountId.Value);
        }
        else if (oidcState.FlowType == OidcFlowType.Login)
        {
            // Login/Registration flow
            if (!string.IsNullOrEmpty(oidcState.DeviceId))
            {
                callbackData.State = oidcState.DeviceId;
            }

            // Store return URL if provided
            if (string.IsNullOrEmpty(oidcState.ReturnUrl) || oidcState.ReturnUrl == "/")
                return await HandleLoginOrRegistration(provider, oidcService, callbackData);
            var returnUrlKey = $"{ReturnUrlCachePrefix}{callbackData.State}";
            await cache.SetAsync(returnUrlKey, oidcState.ReturnUrl, StateExpiration);

            return await HandleLoginOrRegistration(provider, oidcService, callbackData);
        }

        return BadRequest("Unsupported flow type");
    }

    private async Task<IActionResult> HandleManualConnection(
        string provider,
        OidcService oidcService,
        OidcCallbackData callbackData,
        Guid accountId
    )
    {
        provider = provider.ToLower();

        OidcUserInfo userInfo;
        try
        {
            userInfo = await oidcService.ProcessCallbackAsync(callbackData);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error processing {provider} authentication: {ex.Message}");
        }

        if (string.IsNullOrEmpty(userInfo.UserId))
        {
            return BadRequest($"{provider} did not return a valid user identifier.");
        }

        // Extract device ID from the callback state if available
        var deviceId = !string.IsNullOrEmpty(callbackData.State) ? callbackData.State : string.Empty;

        // Check if this provider account is already connected to any user
        var existingConnection = await db.AccountConnections
            .FirstOrDefaultAsync(c =>
                c.Provider == provider &&
                c.ProvidedIdentifier == userInfo.UserId);

        // If it's connected to a different user, return error
        if (existingConnection != null && existingConnection.AccountId != accountId)
        {
            return BadRequest($"This {provider} account is already linked to another user.");
        }

        // Check if the current user already has this provider connected
        var userHasProvider = await db.AccountConnections
            .AnyAsync(c =>
                c.AccountId == accountId &&
                c.Provider == provider);

        if (userHasProvider)
        {
            // Update existing connection with new tokens
            var connection = await db.AccountConnections
                .FirstOrDefaultAsync(c =>
                    c.AccountId == accountId &&
                    c.Provider == provider);

            if (connection != null)
            {
                connection.AccessToken = userInfo.AccessToken;
                connection.RefreshToken = userInfo.RefreshToken;
                connection.LastUsedAt = SystemClock.Instance.GetCurrentInstant();
                connection.Meta = userInfo.ToMetadata();
            }
        }
        else
        {
            // Create new connection
            db.AccountConnections.Add(new SnAccountConnection
            {
                AccountId = accountId,
                Provider = provider,
                ProvidedIdentifier = userInfo.UserId!,
                AccessToken = userInfo.AccessToken,
                RefreshToken = userInfo.RefreshToken,
                LastUsedAt = SystemClock.Instance.GetCurrentInstant(),
                Meta = userInfo.ToMetadata(),
            });
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return StatusCode(500, $"Failed to save {provider} connection. Please try again.");
        }

        // Clean up and redirect
        var returnUrlKey = $"{ReturnUrlCachePrefix}{callbackData.State}";
        var returnUrl = await cache.GetAsync<string>(returnUrlKey);
        await cache.RemoveAsync(returnUrlKey);

        return Redirect(string.IsNullOrEmpty(returnUrl) ? "/auth/callback" : returnUrl);
    }

    private async Task<IActionResult> HandleLoginOrRegistration(
        string provider,
        OidcService oidcService,
        OidcCallbackData callbackData
    )
    {
        OidcUserInfo userInfo;
        try
        {
            userInfo = await oidcService.ProcessCallbackAsync(callbackData);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error processing callback: {ex.Message}");
        }

        if (string.IsNullOrEmpty(userInfo.Email) || string.IsNullOrEmpty(userInfo.UserId))
        {
            return BadRequest($"Email or user ID is missing from {provider}'s response");
        }

        var connection = await db.AccountConnections
            .Include(c => c.Account)
            .FirstOrDefaultAsync(c => c.Provider == provider && c.ProvidedIdentifier == userInfo.UserId);

        var clock = SystemClock.Instance;
        if (connection != null)
        {
            // Login existing user
            var deviceId = !string.IsNullOrEmpty(callbackData.State) ? 
                callbackData.State.Split('|').FirstOrDefault() : 
                string.Empty;
                
            var challenge = await oidcService.CreateChallengeForUserAsync(
                userInfo, 
                connection.Account, 
                HttpContext, 
                deviceId ?? string.Empty);
            return Redirect($"/auth/callback?challenge={challenge.Id}");
        }

        // Register new user
        var account = await accounts.LookupAccount(userInfo.Email) ?? await accounts.CreateAccount(userInfo);

        // Create connection for new or existing user
        var newConnection = new SnAccountConnection
        {
            Account = account,
            Provider = provider,
            ProvidedIdentifier = userInfo.UserId!,
            AccessToken = userInfo.AccessToken,
            RefreshToken = userInfo.RefreshToken,
            LastUsedAt = clock.GetCurrentInstant(),
            Meta = userInfo.ToMetadata()
        };
        db.AccountConnections.Add(newConnection);

        await db.SaveChangesAsync();

        var loginSession = await auth.CreateSessionForOidcAsync(account, clock.GetCurrentInstant());
        var loginToken = auth.CreateToken(loginSession);
        return Redirect($"/auth/callback?token={loginToken}");
    }

    private static async Task<OidcCallbackData> ExtractCallbackData(HttpRequest request)
    {
        var data = new OidcCallbackData();
        switch (request.Method)
        {
            case "GET":
                data.Code = Uri.UnescapeDataString(request.Query["code"].FirstOrDefault() ?? "");
                data.IdToken = Uri.UnescapeDataString(request.Query["id_token"].FirstOrDefault() ?? "");
                data.State = Uri.UnescapeDataString(request.Query["state"].FirstOrDefault() ?? "");
                break;
            case "POST" when request.HasFormContentType:
            {
                var form = await request.ReadFormAsync();
                data.Code = Uri.UnescapeDataString(form["code"].FirstOrDefault() ?? "");
                data.IdToken = Uri.UnescapeDataString(form["id_token"].FirstOrDefault() ?? ""); 
                data.State = Uri.UnescapeDataString(form["state"].FirstOrDefault() ?? "");
                if (form.ContainsKey("user"))
                    data.RawData = Uri.UnescapeDataString(form["user"].FirstOrDefault() ?? "");

                break;
            }
        }

        return data;
    }
}