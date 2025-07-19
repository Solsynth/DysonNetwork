using DysonNetwork.Pass.Account;
using DysonNetwork.Shared.Cache;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Pass.Auth.OpenId;

[ApiController]
[Route("/api/auth/login")]
public class OidcController(
    IServiceProvider serviceProvider,
    AppDatabase db,
    AccountService accounts,
    ICacheService cache
)
    : ControllerBase
{
    private const string StateCachePrefix = "oidc-state:";
    private static readonly TimeSpan StateExpiration = TimeSpan.FromMinutes(15);

    [HttpGet("{provider}")]
    public async Task<ActionResult> OidcLogin(
        [FromRoute] string provider,
        [FromQuery] string? returnUrl = "/",
        [FromHeader(Name = "X-Device-Id")] string? deviceId = null
    )
    {
        try
        {
            var oidcService = GetOidcService(provider);

            // If the user is already authenticated, treat as an account connection request
            if (HttpContext.Items["CurrentUser"] is Account.Account currentUser)
            {
                var state = Guid.NewGuid().ToString();
                var nonce = Guid.NewGuid().ToString();

                // Create and store connection state
                var oidcState = OidcState.ForConnection(currentUser.Id, provider, nonce, deviceId);
                await cache.SetAsync($"{StateCachePrefix}{state}", oidcState, StateExpiration);

                // The state parameter sent to the provider is the GUID key for the cache.
                var authUrl = oidcService.GetAuthorizationUrl(state, nonce);
                return Redirect(authUrl);
            }
            else // Otherwise, proceed with the login / registration flow
            {
                var nonce = Guid.NewGuid().ToString();
                var state = Guid.NewGuid().ToString();

                // Create login state with return URL and device ID
                var oidcState = OidcState.ForLogin(returnUrl ?? "/", deviceId);
                await cache.SetAsync($"{StateCachePrefix}{state}", oidcState, StateExpiration);
                var authUrl = oidcService.GetAuthorizationUrl(state, nonce);
                return Redirect(authUrl);
            }
        }
        catch (Exception ex)
        {
            return BadRequest($"Error initiating OpenID Connect flow: {ex.Message}");
        }
    }

    /// <summary>
    /// Mobile Apple Sign In endpoint
    /// Handles Apple authentication directly from mobile apps
    /// </summary>
    [HttpPost("apple/mobile")]
    public async Task<ActionResult<AuthChallenge>> AppleMobileLogin(
        [FromBody] AppleMobileSignInRequest request
    )
    {
        try
        {
            // Get Apple OIDC service
            if (GetOidcService("apple") is not AppleOidcService appleService)
                return StatusCode(503, "Apple OIDC service not available");

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

            // Create session using the OIDC service
            var challenge = await appleService.CreateChallengeForUserAsync(
                userInfo,
                account,
                HttpContext,
                request.DeviceId
            );

            return Ok(challenge);
        }
        catch (SecurityTokenValidationException ex)
        {
            return Unauthorized($"Invalid identity token: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Log the error
            return StatusCode(500, $"Authentication failed: {ex.Message}");
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
            "afdian" => serviceProvider.GetRequiredService<AfdianOidcService>(),
            _ => throw new ArgumentException($"Unsupported provider: {provider}")
        };
    }

    private async Task<Account.Account> FindOrCreateAccount(OidcUserInfo userInfo, string provider)
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

            var connection = new AccountConnection
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

            return existingAccount;
        }

        // Create new account using the AccountService
        var newAccount = await accounts.CreateAccount(userInfo);

        // Create the provider connection
        var newConnection = new AccountConnection
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

        return newAccount;
    }
}