using DysonNetwork.Padlock.Account;
using DysonNetwork.Padlock.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Cache;
using Microsoft.AspNetCore.WebUtilities;
using NodaTime;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Padlock.Auth.OpenId;

[ApiController]
[Route("/api/connections")]
[Authorize]
public class ConnectionController(
    AppDatabase db,
    IEnumerable<OidcService> oidcServices,
    AccountService accounts,
    AuthService auth,
    ActionLogService actionLogs,
    ICacheService cache,
    IConfiguration configuration,
    ILogger<ConnectionController> logger
) : ControllerBase
{
    public class SetConnectionVisibilityRequest
    {
        public bool IsPublic { get; set; }
    }

    private const string StateCachePrefix = "oidc-state:";
    private const string ReturnUrlCachePrefix = "oidc-returning:";
    private static readonly TimeSpan StateExpiration = TimeSpan.FromMinutes(15);
    private readonly string _cookieDomain = configuration["AuthToken:CookieDomain"]!;

    [HttpGet]
    public async Task<ActionResult<List<SnAccountConnection>>> GetConnections()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

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
                c.IsPublic,
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
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var connection = await db.AccountConnections
            .Where(c => c.Id == id && c.AccountId == currentUser.Id)
            .FirstOrDefaultAsync();
        if (connection == null)
            return NotFound(new ApiError { Code = "CONNECTION_NOT_FOUND", Message = "Account connection was not found.", Status = 404 });

        db.AccountConnections.Remove(connection);
        await db.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id:guid}/visibility")]
    public async Task<ActionResult<SnAccountConnection>> SetConnectionVisibility(
        Guid id,
        [FromBody] SetConnectionVisibilityRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var connection = await db.AccountConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.AccountId == currentUser.Id);
        if (connection is null)
            return NotFound(new ApiError { Code = "CONNECTION_NOT_FOUND", Message = "Account connection was not found.", Status = 404 });

        connection.IsPublic = request.IsPublic;
        await db.SaveChangesAsync();

        return Ok(connection);
    }

    [HttpPost("/api/auth/connect/apple/mobile")]
    public async Task<ActionResult> ConnectAppleMobile([FromBody] AppleMobileConnectRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "AUTH_UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        if (GetOidcService("apple") is not AppleOidcService appleService)
            return StatusCode(503, new ApiError { Code = "OIDC_APPLE_UNAVAILABLE", Message = "Apple OIDC service not available.", Status = 503 });

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
            return BadRequest(new ApiError { Code = "OIDC_APPLE_TOKEN_PROCESS_FAILED", Message = $"Error processing Apple token: {ex.Message}", Status = 400 });
        }

        var existingConnection = await db.AccountConnections
            .FirstOrDefaultAsync(c =>
                c.Provider == "apple" &&
                c.ProvidedIdentifier == userInfo.UserId);

        if (existingConnection != null)
        {
            return BadRequest(new ApiError
            {
                Code = "OIDC_APPLE_ALREADY_LINKED",
                Message = $"This Apple account is already linked to {(existingConnection.AccountId == currentUser.Id ? "your account" : "another user")}.",
                Status = 400
            });
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

        await actionLogs.CreateActionLogAsync(
            currentUser.Id,
            ActionLogType.AccountConnectionLink,
            new Dictionary<string, object> { ["provider"] = "apple" },
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString()
        );

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
            return BadRequest(new ApiError { Code = "OIDC_PROVIDER_NOT_SUPPORTED", Message = $"Provider '{provider}' is not supported.", Status = 400 });

        var callbackData = await ExtractCallbackData(Request);
        if (callbackData.State == null)
            return BadRequest(new ApiError { Code = "OIDC_STATE_MISSING", Message = "State parameter is missing.", Status = 400 });
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
                return BadRequest(new ApiError { Code = "OIDC_STATE_INVALID", Message = "Invalid or expired state parameter.", Status = 400 });
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

        return BadRequest(new ApiError { Code = "OIDC_UNSUPPORTED_FLOW_TYPE", Message = "Unsupported flow type.", Status = 400 });
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
            return BadRequest(new ApiError { Code = "OIDC_CALLBACK_PROCESS_FAILED", Message = $"Error processing {provider} authentication: {ex.Message}", Status = 400 });
        }

        if (string.IsNullOrEmpty(userInfo.UserId))
        {
            return BadRequest(new ApiError { Code = "OIDC_MISSING_USER_ID", Message = $"{provider} did not return a valid user identifier.", Status = 400 });
        }

        // Check if this provider account is already connected to any user
        var existingConnection = await db.AccountConnections
            .FirstOrDefaultAsync(c =>
                c.Provider == provider &&
                c.ProvidedIdentifier == userInfo.UserId);

        // If it's connected to a different user, return error
        if (existingConnection != null && existingConnection.AccountId != accountId)
        {
            return BadRequest(new ApiError { Code = "OIDC_ACCOUNT_ALREADY_LINKED", Message = $"This {provider} account is already linked to another user.", Status = 400 });
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
            return StatusCode(500, new ApiError { Code = "OIDC_SAVE_CONNECTION_FAILED", Message = $"Failed to save {provider} connection. Please try again.", Status = 500 });
        }

        if (!userHasProvider)
        {
            await actionLogs.CreateActionLogAsync(
                accountId,
                ActionLogType.AccountConnectionLink,
                new Dictionary<string, object> { ["provider"] = provider },
                Request.Headers.UserAgent.ToString(),
                HttpContext.Connection.RemoteIpAddress?.ToString()
            );
        }

        // Clean up and redirect
        var returnUrlKey = $"{ReturnUrlCachePrefix}{stateToken}";
        var returnUrl = await cache.GetAsync<string>(returnUrlKey);
        await cache.RemoveAsync(returnUrlKey);

        var siteUrl = configuration["SiteUrl"];
        var redirectUrl = string.IsNullOrEmpty(returnUrl) ? siteUrl + "/auth/success" : returnUrl;
        
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
            return BadRequest(new ApiError { Code = "OIDC_CALLBACK_PROCESS_FAILED", Message = $"Error processing callback: {ex.Message}", Status = 400 });
        }

        if (string.IsNullOrEmpty(userInfo.Email) || string.IsNullOrEmpty(userInfo.UserId))
        {
            return BadRequest(new ApiError { Code = "OIDC_MISSING_EMAIL_OR_USER_ID", Message = $"Email or user ID is missing from {provider}'s response.", Status = 400 });
        }
        
        // Retrieve and clean up the return URL
        var returnUrlKey = $"{ReturnUrlCachePrefix}{stateToken}";
        var returnUrl = await cache.GetAsync<string>(returnUrlKey);
        await cache.RemoveAsync(returnUrlKey);

        var siteUrl = configuration["SiteUrl"];
        var redirectBaseUrl = string.IsNullOrEmpty(returnUrl) ? siteUrl + "/auth/success" : returnUrl;

        var connection = await db.AccountConnections
            .Include(c => c.Account)
            .FirstOrDefaultAsync(c => c.Provider == provider && c.ProvidedIdentifier == userInfo.UserId);

        var clock = SystemClock.Instance;
        
        if (connection != null)
        {
            // Login existing user
            var parentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;
            
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

        var registrationParentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;

        var loginSession = await oidcService.CreateSessionForUserAsync(
            userInfo,
            account,
            HttpContext,
            deviceId ?? string.Empty,
            null,
            ClientPlatform.Web,
            registrationParentSession);
        var pair2 = await auth.CreateTokenPair(loginSession);
        AppendAuthCookies(pair2);

        var finalRedirectUrl = BuildLoginRedirectUrl(redirectBaseUrl, pair2);
        logger.LogInformation("OIDC registration successful for new user {UserId}. Redirecting to {RedirectUrl}", account.Id, finalRedirectUrl);
        return Redirect(finalRedirectUrl);
    }

    private string BuildLoginRedirectUrl(string redirectBaseUrl, AuthService.TokenPair pair)
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

    private void AppendAuthCookies(AuthService.TokenPair pair)
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
