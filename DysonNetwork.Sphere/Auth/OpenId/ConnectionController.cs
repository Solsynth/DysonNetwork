using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Auth.OpenId;

[ApiController]
[Route("/api/accounts/me/connections")]
[Authorize]
public class ConnectionController(
    AppDatabase db, 
    IEnumerable<OidcService> oidcServices, 
    AccountService accountService, 
    AuthService authService,
    IClock clock
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AccountConnection>>> GetConnections()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var connections = await db.AccountConnections
            .Where(c => c.AccountId == currentUser.Id)
            .Select(c => new { c.Id, c.AccountId, c.Provider, c.ProvidedIdentifier })
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

        var oidcService = oidcServices.FirstOrDefault(s => s.ProviderName.Equals(request.Provider, StringComparison.OrdinalIgnoreCase));
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
        var oidcService = oidcServices.FirstOrDefault(s => s.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase));
        if (oidcService == null)
            return BadRequest($"Provider '{provider}' is not supported.");

        var callbackData = await ExtractCallbackData(Request);
        if (callbackData.State == null)
            return BadRequest("State parameter is missing.");

        var sessionState = HttpContext.Session.GetString($"oidc_state_{callbackData.State!}");
        HttpContext.Session.Remove($"oidc_state_{callbackData.State}");

        // If sessionState is present, it's a manual connection flow for an existing user.
        if (sessionState != null)
        {
            var stateParts = sessionState.Split('|');
            if (stateParts.Length != 3 || !stateParts[1].Equals(provider, StringComparison.OrdinalIgnoreCase))
                return BadRequest("State mismatch.");

            var accountId = Guid.Parse(stateParts[0]);
            return await HandleManualConnection(provider, oidcService, callbackData, accountId);
        }
        
        // Otherwise, it's a login or registration flow.
        return await HandleLoginOrRegistration(provider, oidcService, callbackData);
    }

    private async Task<IActionResult> HandleManualConnection(string provider, OidcService oidcService, OidcCallbackData callbackData, Guid accountId)
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

        var existingConnection = await db.AccountConnections
            .FirstOrDefaultAsync(c => c.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase) && c.ProvidedIdentifier == userInfo.UserId);

        if (existingConnection != null && existingConnection.AccountId != accountId)
        {
            return BadRequest($"This {provider} account is already linked to another user.");
        }

        var userConnection = await db.AccountConnections
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

        if (userConnection != null)
        {
            userConnection.AccessToken = userInfo.AccessToken;
            userConnection.RefreshToken = userInfo.RefreshToken;
            userConnection.LastUsedAt = clock.GetCurrentInstant();
        }
        else
        {
            db.AccountConnections.Add(new AccountConnection
            {
                AccountId = accountId,
                Provider = provider,
                ProvidedIdentifier = userInfo.UserId!,
                AccessToken = userInfo.AccessToken,
                RefreshToken = userInfo.RefreshToken,
                LastUsedAt = clock.GetCurrentInstant()
            });
        }

        await db.SaveChangesAsync();

        var returnUrl = HttpContext.Session.GetString($"oidc_return_url_{callbackData.State}");
        HttpContext.Session.Remove($"oidc_return_url_{callbackData.State}");

        return Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
    }

    private async Task<IActionResult> HandleLoginOrRegistration(string provider, OidcService oidcService, OidcCallbackData callbackData)
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

        if (connection != null)
        {
            // Login existing user
            var session = await authService.CreateSessionAsync(connection.Account, clock.GetCurrentInstant());
            var token = authService.CreateToken(session);
            return Redirect($"/?token={token}");
        }

        var account = await accountService.LookupAccount(userInfo.Email);
        if (account == null)
        {
            // Register new user
            account = await accountService.CreateAccount(userInfo);
        }

        if (account == null)
        {
            return BadRequest("Unable to create or link account.");
        }

        // Create connection for new or existing user
        var newConnection = new AccountConnection
        {
            Account = account,
            Provider = provider,
            ProvidedIdentifier = userInfo.UserId!,
            AccessToken = userInfo.AccessToken,
            RefreshToken = userInfo.RefreshToken,
            LastUsedAt = clock.GetCurrentInstant()
        };
        db.AccountConnections.Add(newConnection);

        await db.SaveChangesAsync();

        var loginSession = await authService.CreateSessionAsync(account, clock.GetCurrentInstant());
        var loginToken = authService.CreateToken(loginSession);
        return Redirect($"/?token={loginToken}");
    }

    private async Task<OidcCallbackData> ExtractCallbackData(HttpRequest request)
    {
        var data = new OidcCallbackData();
        if (request.Method == "GET")
        {
            data.Code = request.Query["code"].FirstOrDefault() ?? "";
            data.IdToken = request.Query["id_token"].FirstOrDefault() ?? "";
            data.State = request.Query["state"].FirstOrDefault();
        }
        else if (request.Method == "POST" && request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            data.Code = form["code"].FirstOrDefault() ?? "";
            data.IdToken = form["id_token"].FirstOrDefault() ?? "";
            data.State = form["state"].FirstOrDefault();
            if (form.ContainsKey("user"))
            {
                data.RawData = form["user"].FirstOrDefault();
            }
        }

        return data;
    }


}