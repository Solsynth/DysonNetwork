using DysonNetwork.Padlock.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Cache;
using Microsoft.AspNetCore.WebUtilities;
using NodaTime;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.Auth.OpenId;

[ApiController]
[Route("/api/connections")]
[Authorize]
public class ConnectionController(
    AppDatabase db,
    IEnumerable<OidcService> oidcServices,
    AccountService accounts,
    AuthService auth,
    ICacheService cache,
    IConfiguration configuration,
    ILogger<ConnectionController> logger
) : ControllerBase
{
    private const string StateCachePrefix = "oidc-state:";
    private const string ReturnUrlCachePrefix = "oidc-returning:";
    private static readonly TimeSpan StateExpiration = TimeSpan.FromMinutes(15);
    private readonly string _cookieDomain = configuration["AuthToken:CookieDomain"]!;

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
    [Route("/api/auth/callback/{provider}")]
    [HttpGet, HttpPost]
    public async Task<IActionResult> HandleCallback([FromRoute] string provider)
    {
        var oidcService = GetOidcService(provider);
        if (oidcService == null)
            return BadRequest($"Provider '{provider}' is not supported.");

        var callbackData = await ExtractCallbackData(Request);
        if (callbackData.State == null)
            return BadRequest("State parameter is missing.");
        var stateToken = callbackData.State;

        // Get the state from the cache
        var stateKey = $"{StateCachePrefix}{stateToken}";

        // Try to get the state as OidcState first (new format)
        var oidcState = await cache.GetAsync<OidcState>(stateKey);

        // If not found, try to get as string (legacy format)
        if (oidcState == null)
        {
            var stateValue = await cache.GetAsync<string>(stateKey);
            if (string.IsNullOrEmpty(stateValue) || !OidcState.TryParse(stateValue, out oidcState) || oidcState == null)
            {
                logger.LogWarning("Invalid or expired OIDC state: {State}", stateToken);
                return BadRequest("Invalid or expired state parameter");
            }
        }
        
        logger.LogInformation("OIDC callback for provider {Provider} with state {State} and flow {FlowType}", provider, stateToken, oidcState.FlowType);

        // Remove the state from cache to prevent replay attacks
        await cache.RemoveAsync(stateKey);

        // Handle the flow based on state type
        if (oidcState is { FlowType: OidcFlowType.Connect, AccountId: not null })
        {
            // Connection flow
            return await HandleManualConnection(
                provider,
                oidcService,
                callbackData,
                oidcState.AccountId.Value,
                stateToken
            );
        }

        if (oidcState.FlowType == OidcFlowType.Login)
        {
            // Store return URL if provided
            if (string.IsNullOrEmpty(oidcState.ReturnUrl) || oidcState.ReturnUrl == "/")
            {
                logger.LogInformation("No returnUrl provided in OIDC state, will use default.");
                return await HandleLoginOrRegistration(
                    provider,
                    oidcService,
                    callbackData,
                    oidcState.DeviceId,
                    stateToken
                );
            }
            
            logger.LogInformation("Storing returnUrl {ReturnUrl} for state {State}", oidcState.ReturnUrl, stateToken);
            var returnUrlKey = $"{ReturnUrlCachePrefix}{stateToken}";
            await cache.SetAsync(returnUrlKey, oidcState.ReturnUrl, StateExpiration);

            return await HandleLoginOrRegistration(
                provider,
                oidcService,
                callbackData,
                oidcState.DeviceId,
                stateToken
            );
        }

        return BadRequest("Unsupported flow type");
    }

    private async Task<IActionResult> HandleManualConnection(
        string provider,
        OidcService oidcService,
        OidcCallbackData callbackData,
        Guid accountId,
        string stateToken
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
            logger.LogError(ex, "Error processing OIDC callback for provider {Provider} during connection flow", provider);
            return BadRequest($"Error processing {provider} authentication: {ex.Message}");
        }

        if (string.IsNullOrEmpty(userInfo.UserId))
        {
            return BadRequest($"{provider} did not return a valid user identifier.");
        }

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
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Failed to save OIDC connection for provider {Provider}", provider);
            return StatusCode(500, $"Failed to save {provider} connection. Please try again.");
        }

        // Clean up and redirect
        var returnUrlKey = $"{ReturnUrlCachePrefix}{stateToken}";
        var returnUrl = await cache.GetAsync<string>(returnUrlKey);
        await cache.RemoveAsync(returnUrlKey);

        var siteUrl = configuration["SiteUrl"];
        var redirectUrl = string.IsNullOrEmpty(returnUrl) ? siteUrl + "/auth/callback" : returnUrl;
        
        logger.LogInformation("Redirecting after OIDC connection to {RedirectUrl}", redirectUrl);
        return Redirect(redirectUrl);
    }

    private async Task<IActionResult> HandleLoginOrRegistration(
        string provider,
        OidcService oidcService,
        OidcCallbackData callbackData,
        string? deviceId,
        string stateToken
    )
    {
        OidcUserInfo userInfo;
        try
        {
            userInfo = await oidcService.ProcessCallbackAsync(callbackData);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing OIDC callback for provider {Provider} during login/registration flow", provider);
            return BadRequest($"Error processing callback: {ex.Message}");
        }

        if (string.IsNullOrEmpty(userInfo.Email) || string.IsNullOrEmpty(userInfo.UserId))
        {
            return BadRequest($"Email or user ID is missing from {provider}'s response");
        }
        
        // Retrieve and clean up the return URL
        var returnUrlKey = $"{ReturnUrlCachePrefix}{stateToken}";
        var returnUrl = await cache.GetAsync<string>(returnUrlKey);
        await cache.RemoveAsync(returnUrlKey);

        var siteUrl = configuration["SiteUrl"];
        var redirectBaseUrl = string.IsNullOrEmpty(returnUrl) ? siteUrl + "/auth/callback" : returnUrl;

        var connection = await db.AccountConnections
            .Include(c => c.Account)
            .FirstOrDefaultAsync(c => c.Provider == provider && c.ProvidedIdentifier == userInfo.UserId);

        var clock = SystemClock.Instance;
        
        if (connection != null)
        {
            // Login existing user
            if (HttpContext.Items["CurrentSession"] is not SnAuthSession parentSession) parentSession = null;
            
            var session = await oidcService.CreateSessionForUserAsync(
                userInfo,
                connection.Account,
                HttpContext,
                deviceId ?? string.Empty,
                null,
                ClientPlatform.Web,
                parentSession);

            var pair = await auth.CreateTokenPair(session);
            AppendAuthCookies(pair);
            var redirectUrl = BuildLoginRedirectUrl(redirectBaseUrl, pair);
            logger.LogInformation("OIDC login successful for user {UserId}. Redirecting to {RedirectUrl}", connection.AccountId, redirectUrl);
            return Redirect(redirectUrl);
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

        if (HttpContext.Items["CurrentSession"] is not SnAuthSession registrationParentSession) registrationParentSession = null;

        var loginSession = await oidcService.CreateSessionForUserAsync(
            userInfo,
            account,
            HttpContext,
            deviceId ?? string.Empty,
            null,
            ClientPlatform.Web,
            registrationParentSession);
        var pair = await auth.CreateTokenPair(loginSession);
        AppendAuthCookies(pair);

        var finalRedirectUrl = BuildLoginRedirectUrl(redirectBaseUrl, pair);
        logger.LogInformation("OIDC registration successful for new user {UserId}. Redirecting to {RedirectUrl}", account.Id, finalRedirectUrl);
        return Redirect(finalRedirectUrl);
    }

    private string BuildLoginRedirectUrl(string redirectBaseUrl, TokenPair pair)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var redirectUrl = QueryHelpers.AddQueryString(redirectBaseUrl, "token", pair.AccessToken);
        redirectUrl = QueryHelpers.AddQueryString(redirectUrl, "refreshToken", pair.RefreshToken);
        redirectUrl = QueryHelpers.AddQueryString(
            redirectUrl,
            "expiresIn",
            ((long)Math.Max(0, (pair.AccessTokenExpiresAt - now).TotalSeconds)).ToString()
        );
        redirectUrl = QueryHelpers.AddQueryString(
            redirectUrl,
            "refreshExpiresIn",
            ((long)Math.Max(0, (pair.RefreshTokenExpiresAt - now).TotalSeconds)).ToString()
        );
        return redirectUrl;
    }

    private void AppendAuthCookies(TokenPair pair)
    {
        Response.Cookies.Append(AuthConstants.CookieTokenName, pair.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Domain = _cookieDomain,
            Expires = pair.AccessTokenExpiresAt.ToDateTimeOffset()
        });
        Response.Cookies.Append(AuthConstants.RefreshCookieTokenName, pair.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Domain = _cookieDomain,
            Expires = pair.RefreshTokenExpiresAt.ToDateTimeOffset()
        });
    }

    private static async Task<OidcCallbackData> ExtractCallbackData(HttpRequest request)
    {
        var data = new OidcCallbackData();

        // Extract data based on request method
        if (request.Method == "GET")
        {
            // Extract from query string
            data.Code = Uri.UnescapeDataString(request.Query["code"].FirstOrDefault() ?? "");
            data.IdToken = Uri.UnescapeDataString(request.Query["id_token"].FirstOrDefault() ?? "");
            data.State = Uri.UnescapeDataString(request.Query["state"].FirstOrDefault() ?? "");

            // Populate all query parameters for providers that need them (like Steam OpenID)
            foreach (var param in request.Query)
            {
                data.QueryParameters[param.Key] = Uri.UnescapeDataString(param.Value.FirstOrDefault() ?? "");
            }
        }
        else if (request.Method == "POST" && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            data.Code = Uri.UnescapeDataString(form["code"].FirstOrDefault() ?? "");
            data.IdToken = Uri.UnescapeDataString(form["id_token"].FirstOrDefault() ?? "");
            data.State = Uri.UnescapeDataString(form["state"].FirstOrDefault() ?? "");

            if (form.ContainsKey("user"))
                data.RawData = Uri.UnescapeDataString(form["user"].FirstOrDefault() ?? "");

            // Populate all form parameters
            foreach (var param in form)
            {
                data.QueryParameters[param.Key] = Uri.UnescapeDataString(param.Value.FirstOrDefault() ?? "");
            }
        }

        return data;
    }
}
