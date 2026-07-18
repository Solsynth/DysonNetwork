using DysonNetwork.Padlock.Account;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Padlock.Auth.OpenId;

[ApiController]
[Route("/api/auth/login")]
public class OidcController(
    IServiceProvider serviceProvider,
    AppDatabase db,
    AccountService accounts,
    AuthService auth,
    ActionLogService actionLogs,
    ICacheService cache,
    IConfiguration configuration,
    ILogger<OidcController> logger
)
    : ControllerBase
{
    private const string StateCachePrefix = "oidc-state:";
    private static readonly TimeSpan StateExpiration = TimeSpan.FromMinutes(15);
    private readonly string _cookieDomain = configuration["AuthToken:CookieDomain"]!;

    [HttpGet("{provider}")]
    public async Task<ActionResult> OidcLogin(
        [FromRoute] string provider,
        [FromQuery] string? returnUrl = "/",
        [FromQuery] string? deviceId = null,
        [FromQuery] string? flow = null
    )
    {
        logger.LogInformation("OIDC login request for provider {Provider} with returnUrl {ReturnUrl}, deviceId {DeviceId} and flow {Flow}", provider, returnUrl, deviceId, flow);
        try
        {
            var oidcService = GetOidcService(provider);

            // If the user is already authenticated, treat as an account connection request
            if (flow != "login" && HttpContext.Items["CurrentUser"] is SnAccount currentUser)
            {
                var state = Guid.NewGuid().ToString();
                var nonce = Guid.NewGuid().ToString();

                // Create and store connection state
                var oidcState = OidcState.ForConnection(currentUser.Id, provider, nonce, deviceId);
                await cache.SetAsync($"{StateCachePrefix}{state}", oidcState, StateExpiration);
                logger.LogInformation("OIDC connection flow started for user {UserId} with state {State}", currentUser.Id, state);

                // The state parameter sent to the provider is the GUID key for the cache.
                var authUrl = await oidcService.GetAuthorizationUrlAsync(state, nonce);
                return Redirect(authUrl);
            }
            else // Otherwise, proceed with the login / registration flow
            {
                var nonce = Guid.NewGuid().ToString();
                var state = Guid.NewGuid().ToString();

                // Create login state with return URL and device ID
                var oidcState = OidcState.ForLogin(returnUrl ?? "/", deviceId);
                await cache.SetAsync($"{StateCachePrefix}{state}", oidcState, StateExpiration);
                logger.LogInformation("OIDC login flow started with state {State} and returnUrl {ReturnUrl}", state, oidcState.ReturnUrl);
                var authUrl = await oidcService.GetAuthorizationUrlAsync(state, nonce);
                return Redirect(authUrl);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initiating OIDC flow for provider {Provider}", provider);
            return BadRequest(new ApiError { Code = "OIDC_INIT_FLOW_FAILED", Message = $"Error initiating OpenID Connect flow: {ex.Message}", Status = 400 });
        }
    }

    /// <summary>
    /// Mobile Apple Sign In endpoint
    /// Handles Apple authentication directly from mobile apps
    /// </summary>
    [HttpPost("apple/mobile")]
    public async Task<ActionResult<TokenExchangeResponse>> AppleMobileLogin(
        [FromBody] AppleMobileSignInRequest request
    )
    {
        try
        {
            // Get Apple OIDC service
            if (GetOidcService("apple") is not AppleOidcService appleService)
                return StatusCode(503, new ApiError { Code = "OIDC_APPLE_UNAVAILABLE", Message = "Apple OIDC service not available.", Status = 503 });

            // Prepare callback data for processing
            var callbackData = new OidcCallbackData
            {
                IdToken = request.IdentityToken,
                Code = request.AuthorizationCode,
            };

            // Process the authentication
            var userInfo = await appleService.ProcessCallbackAsync(callbackData);

            // Find or create user account using existing logic
            var account = await FindOrCreateAccount(userInfo, "apple");

            var parentSession = HttpContext.Items["CurrentSession"] as SnAuthSession;
            
            // Create session using the OIDC service
            var session = await appleService.CreateSessionForUserAsync(
                userInfo,
                account,
                HttpContext,
                request.DeviceId,
                request.DeviceName,
                ClientPlatform.Ios,
                parentSession
            );

            var pair = await auth.CreateTokenPair(session);
            AppendAuthCookies(pair);

            var now = SystemClock.Instance.GetCurrentInstant();
            return Ok(new TokenExchangeResponse
            {
                Token = pair.AccessToken,
                RefreshToken = pair.RefreshToken,
                ExpiresIn = (long)Math.Max(0, (pair.AccessTokenExpiresAt - now).TotalSeconds),
                RefreshExpiresIn = (long)Math.Max(0, (pair.RefreshTokenExpiresAt - now).TotalSeconds)
            });
        }
        catch (SecurityTokenValidationException ex)
        {
            return Unauthorized(new ApiError { Code = "OIDC_INVALID_IDENTITY_TOKEN", Message = $"Invalid identity token: {ex.Message}", Status = 401 });
        }
        catch (Grpc.Core.RpcException ex)
        {
            return BadRequest(new ApiError { Code = "OIDC_AUTH_FAILED", Message = $"Authentication failed: {ex.Status.Detail}", Status = 400 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "OIDC_AUTHENTICATION_FAILED", Message = $"Authentication failed: {ex.Message}", Status = 500 });
        }
    }

    private OidcService GetOidcService(string provider)
    {
        return provider.ToLower() switch
        {
            "apple" => serviceProvider.GetRequiredService<AppleOidcService>(),
            "google" => serviceProvider.GetRequiredService<GoogleOidcService>(),
            "microsoft" => serviceProvider.GetRequiredService<MicrosoftOidcService>(),
            "discord" => serviceProvider.GetRequiredService<DiscordOidcService>(),
            "github" => serviceProvider.GetRequiredService<GitHubOidcService>(),

            "steam" => serviceProvider.GetRequiredService<SteamOidcService>(),
            "afdian" => serviceProvider.GetRequiredService<AfdianOidcService>(),
            _ => throw new ArgumentException($"Unsupported provider: {provider}")
        };
    }

    private async Task<SnAccount> FindOrCreateAccount(OidcUserInfo userInfo, string provider)
    {
        if (string.IsNullOrEmpty(userInfo.Email))
            throw new ArgumentException("Email is required for account creation");

        // Check if an account exists by email
        var existingAccount = await accounts.LookupAccount(userInfo.Email);
        if (existingAccount != null)
        {
            // Check if this provider connection already exists
            var existingConnection = await db.AccountConnections
                .FirstOrDefaultAsync(c => c.AccountId == existingAccount.Id &&
                                          c.Provider == provider &&
                                          c.ProvidedIdentifier == userInfo.UserId);

            // If no connection exists, create one
            if (existingConnection != null)
            {
                await db.AccountConnections
                    .Where(c => c.AccountId == existingAccount.Id &&
                                c.Provider == provider &&
                                c.ProvidedIdentifier == userInfo.UserId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.LastUsedAt, SystemClock.Instance.GetCurrentInstant())
                        .SetProperty(c => c.Meta, userInfo.ToMetadata()));

                return existingAccount;
            }

            var connection = new SnAccountConnection
            {
                AccountId = existingAccount.Id,
                Provider = provider,
                ProvidedIdentifier = userInfo.UserId!,
                AccessToken = userInfo.AccessToken,
                RefreshToken = userInfo.RefreshToken,
                LastUsedAt = SystemClock.Instance.GetCurrentInstant(),
                Meta = userInfo.ToMetadata()
            };

            await db.AccountConnections.AddAsync(connection);
            await db.SaveChangesAsync();

            await actionLogs.CreateActionLogAsync(
                existingAccount.Id,
                ActionLogType.AccountConnectionLink,
                new Dictionary<string, object> { ["provider"] = provider }
            );

            return existingAccount;
        }

        // Create new account using the AccountService
        var newAccount = await accounts.CreateAccount(userInfo);

        // Create the provider connection
        var newConnection = new SnAccountConnection
        {
            AccountId = newAccount.Id,
            Provider = provider,
            ProvidedIdentifier = userInfo.UserId!,
            AccessToken = userInfo.AccessToken,
            RefreshToken = userInfo.RefreshToken,
            LastUsedAt = SystemClock.Instance.GetCurrentInstant(),
            Meta = userInfo.ToMetadata()
        };

        db.AccountConnections.Add(newConnection);
        await db.SaveChangesAsync();

        await actionLogs.CreateActionLogAsync(
            newAccount.Id,
            ActionLogType.AccountConnectionLink,
            new Dictionary<string, object> { ["provider"] = provider }
        );

        return newAccount;
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
}
