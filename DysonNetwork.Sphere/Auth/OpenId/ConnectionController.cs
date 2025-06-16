using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Sphere.Auth.OpenId;
using NodaTime;

namespace DysonNetwork.Sphere.Auth.OpenId;

[ApiController]
[Route("/accounts/me/connections")]
[Authorize]
public class ConnectionController(
    AppDatabase db,
    IEnumerable<OidcService> oidcServices,
    AccountService accounts,
    AuthService auth
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AccountConnection>>> GetConnections()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
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
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
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

    [HttpPost("/auth/connect/apple/mobile")]
    public async Task<ActionResult> ConnectAppleMobile([FromBody] AppleMobileConnectRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
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

        db.AccountConnections.Add(new AccountConnection
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

    /// <summary>
    /// Initiates manual connection to an OAuth provider for the current user
    /// </summary>
    [HttpPost("connect")]
    public async Task<ActionResult<object>> InitiateConnection([FromBody] ConnectProviderRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var oidcService = GetOidcService(request.Provider);
        if (oidcService == null)
            return BadRequest($"Provider '{request.Provider}' is not supported");

        var existingConnection = await db.AccountConnections
            .AnyAsync(c => c.AccountId == currentUser.Id && c.Provider == oidcService.ProviderName);

        if (existingConnection)
            return BadRequest($"You already have a {request.Provider} connection");

        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");
        HttpContext.Session.SetString($"oidc_state_{state}", $"{currentUser.Id}|{request.Provider}|{nonce}");

        var finalReturnUrl = !string.IsNullOrEmpty(request.ReturnUrl) ? request.ReturnUrl : "/settings/connections";
        HttpContext.Session.SetString($"oidc_return_url_{state}", finalReturnUrl);

        var authUrl = oidcService.GetAuthorizationUrl(state, nonce);

        return Ok(new
        {
            authUrl,
            message = $"Redirect to this URL to connect your {request.Provider} account"
        });
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

        var sessionState = HttpContext.Session.GetString($"oidc_state_{callbackData.State!}");
        HttpContext.Session.Remove($"oidc_state_{callbackData.State}");

        // If sessionState is present, it's a manual connection flow for an existing user.
        if (sessionState == null) return await HandleLoginOrRegistration(provider, oidcService, callbackData);
        var stateParts = sessionState.Split('|');
        if (stateParts.Length != 3 || !stateParts[1].Equals(provider, StringComparison.OrdinalIgnoreCase))
            return BadRequest("State mismatch.");

        var accountId = Guid.Parse(stateParts[0]);
        return await HandleManualConnection(provider, oidcService, callbackData, accountId);
    }

    private async Task<IActionResult> HandleManualConnection(string provider, OidcService oidcService,
        OidcCallbackData callbackData, Guid accountId)
    {
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

        // Check if this provider account is already connected to any user
        var existingConnection = await db.AccountConnections
            .FirstOrDefaultAsync(c =>
                c.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) &&
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
                c.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

        if (userHasProvider)
        {
            // Update existing connection with new tokens
            var connection = await db.AccountConnections
                .FirstOrDefaultAsync(c =>
                    c.AccountId == accountId && 
                    c.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

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
            db.AccountConnections.Add(new AccountConnection
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
            return StatusCode(500, $"Failed to save {provider} connection. Please try again.");
        }

        // Clean up and redirect
        var returnUrl = HttpContext.Session.GetString($"oidc_return_url_{callbackData.State}");
        HttpContext.Session.Remove($"oidc_return_url_{callbackData.State}");
        HttpContext.Session.Remove($"oidc_state_{callbackData.State}");

        return Redirect(string.IsNullOrEmpty(returnUrl) ? "/settings/connections" : returnUrl);
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
            var session = await auth.CreateSessionAsync(connection.Account, clock.GetCurrentInstant());
            var token = auth.CreateToken(session);
            return Redirect($"/auth/token?token={token}");
        }

        // Register new user
        var account = await accounts.LookupAccount(userInfo.Email) ?? await accounts.CreateAccount(userInfo);

        // Create connection for new or existing user
        var newConnection = new AccountConnection
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

        var loginSession = await auth.CreateSessionAsync(account, clock.GetCurrentInstant());
        var loginToken = auth.CreateToken(loginSession);
        return Redirect($"/auth/token?token={loginToken}");
    }

    private static async Task<OidcCallbackData> ExtractCallbackData(HttpRequest request)
    {
        var data = new OidcCallbackData();
        switch (request.Method)
        {
            case "GET":
                data.Code = request.Query["code"].FirstOrDefault() ?? "";
                data.IdToken = request.Query["id_token"].FirstOrDefault() ?? "";
                data.State = request.Query["state"].FirstOrDefault();
                break;
            case "POST" when request.HasFormContentType:
            {
                var form = await request.ReadFormAsync();
                data.Code = form["code"].FirstOrDefault() ?? "";
                data.IdToken = form["id_token"].FirstOrDefault() ?? "";
                data.State = form["state"].FirstOrDefault();
                if (form.ContainsKey("user"))
                {
                    data.RawData = form["user"].FirstOrDefault();
                }

                break;
            }
        }

        return data;
    }
}