using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Auth.OpenId;

/// <summary>
/// This controller is designed to handle the OAuth callback.
/// </summary>
[ApiController]
[Route("/auth/callback")]
public class AuthCallbackController(
    AppDatabase db,
    AccountService accounts,
    MagicSpellService spells,
    AuthService auth,
    IServiceProvider serviceProvider,
    Account.AccountUsernameService accountUsernameService
)
    : ControllerBase
{
    [HttpPost("apple")]
    public async Task<ActionResult> AppleCallbackPost(
        [FromForm] string code,
        [FromForm(Name = "id_token")] string idToken,
        [FromForm] string? state = null,
        [FromForm] string? user = null)
    {
        return await ProcessOidcCallback("apple", new OidcCallbackData
        {
            Code = code,
            IdToken = idToken,
            State = state,
            RawData = user
        });
    }

    private async Task<ActionResult> ProcessOidcCallback(string provider, OidcCallbackData callbackData)
    {
        try
        {
            // Get the appropriate provider service
            var oidcService = GetOidcService(provider);

            // Process the callback
            var userInfo = await oidcService.ProcessCallbackAsync(callbackData);

            if (string.IsNullOrEmpty(userInfo.Email) || string.IsNullOrEmpty(userInfo.UserId))
            {
                return BadRequest($"Email or user ID is missing from {provider}'s response");
            }

            // First, check if we already have a connection with this provider ID
            var existingConnection = await db.AccountConnections
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Provider == provider && c.ProvidedIdentifier == userInfo.UserId);

            if (existingConnection is not null)
                return await CreateSessionAndRedirect(
                    oidcService,
                    userInfo,
                    existingConnection.Account,
                    callbackData.State
                );

            // If no existing connection, try to find an account by email
            var account = await accounts.LookupAccount(userInfo.Email);
            if (account == null)
            {
                // Create a new account using the AccountService
                account = await accounts.CreateAccount(userInfo);
            }

            // Create a session for the user
            var session = await oidcService.CreateSessionForUserAsync(userInfo, account);

            // Generate token
            var token = auth.CreateToken(session);

            // Determine where to redirect
            var redirectUrl = "/";
            if (!string.IsNullOrEmpty(callbackData.State))
            {
                // Use state as redirect URL (should be validated in production)
                redirectUrl = callbackData.State;
            }

            // Set the token as a cookie
            Response.Cookies.Append(AuthConstants.TokenQueryParamName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return Redirect(redirectUrl);
        }
        catch (Exception ex)
        {
            return BadRequest($"Error processing {provider} Sign In: {ex.Message}");
        }
    }

    private OidcService GetOidcService(string provider)
    {
        return provider.ToLower() switch
        {
            "apple" => serviceProvider.GetRequiredService<AppleOidcService>(),
            "google" => serviceProvider.GetRequiredService<GoogleOidcService>(),
            // Add more providers as needed
            _ => throw new ArgumentException($"Unsupported provider: {provider}")
        };
    }

    /// <summary>
    /// Creates a session and redirects the user with a token
    /// </summary>
    private async Task<ActionResult> CreateSessionAndRedirect(OidcService oidcService, OidcUserInfo userInfo,
        Account.Account account, string? state)
    {
        // Create a session for the user
        var session = await oidcService.CreateSessionForUserAsync(userInfo, account);

        // Generate token
        var token = auth.CreateToken(session);

        // Determine where to redirect
        var redirectUrl = "/";
        if (!string.IsNullOrEmpty(state))
            redirectUrl = state;

        // Set the token as a cookie
        Response.Cookies.Append(AuthConstants.TokenQueryParamName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        return Redirect(redirectUrl);
    }
}