using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using DysonNetwork.Sphere.Auth.OidcProvider.Services;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Auth.OidcProvider.Responses;
using DysonNetwork.Sphere.Developer;

namespace DysonNetwork.Sphere.Pages.Auth;

public class AuthorizeModel(OidcProviderService oidcService) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true, Name = "client_id")]
    [Required(ErrorMessage = "The client_id parameter is required")]
    public string? ClientIdString { get; set; }

    public Guid ClientId { get; set; }

    [BindProperty(SupportsGet = true, Name = "response_type")]
    public string ResponseType { get; set; } = "code";

    [BindProperty(SupportsGet = true, Name = "redirect_uri")]
    public string? RedirectUri { get; set; }

    [BindProperty(SupportsGet = true)] public string? Scope { get; set; }

    [BindProperty(SupportsGet = true)] public string? State { get; set; }

    [BindProperty(SupportsGet = true)] public string? Nonce { get; set; }


    [BindProperty(SupportsGet = true, Name = "code_challenge")]
    public string? CodeChallenge { get; set; }

    [BindProperty(SupportsGet = true, Name = "code_challenge_method")]
    public string? CodeChallengeMethod { get; set; }

    [BindProperty(SupportsGet = true, Name = "response_mode")]
    public string? ResponseMode { get; set; }

    public string? AppName { get; set; }
    public string? AppLogo { get; set; }
    public string? AppUri { get; set; }
    public string[]? RequestedScopes { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // First check if user is authenticated
        if (HttpContext.Items["CurrentUser"] is not Sphere.Account.Account currentUser)
        {
            var returnUrl = Uri.EscapeDataString($"{Request.Path}{Request.QueryString}");
            return RedirectToPage("/Auth/Login", new { returnUrl });
        }

        // Validate client_id
        if (string.IsNullOrEmpty(ClientIdString) || !Guid.TryParse(ClientIdString, out var clientId))
        {
            ModelState.AddModelError("client_id", "Invalid client_id format");
            return BadRequest("Invalid client_id format");
        }

        ClientId = clientId;

        // Get client info
        var client = await oidcService.FindClientByIdAsync(ClientId);
        if (client == null)
        {
            ModelState.AddModelError("client_id", "Client not found");
            return NotFound("Client not found");
        }

        // Validate redirect URI for non-Developing apps
        if (client.Status != CustomAppStatus.Developing)
        {
            if (!string.IsNullOrEmpty(RedirectUri) && !(client.RedirectUris?.Contains(RedirectUri) ?? false))
            {
                return BadRequest(new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Invalid redirect_uri"
                });
            }
        }

        // Check for an existing valid session
        var existingSession = await oidcService.FindValidSessionAsync(currentUser.Id, clientId);
        if (existingSession != null)
        {
            // Auto-approve since valid session exists
            return await HandleApproval(currentUser, client, existingSession);
        }

        // Show authorization page
        AppName = client.Name;
        AppLogo = client.LogoUri;
        AppUri = client.ClientUri;
        RequestedScopes = (Scope ?? "openid profile").Split(' ').Distinct().ToArray();

        return Page();
    }

    private async Task<IActionResult> HandleApproval(Sphere.Account.Account currentUser, CustomApp client, Session? existingSession = null)
    {
        if (string.IsNullOrEmpty(RedirectUri))
        {
            ModelState.AddModelError("redirect_uri", "No redirect_uri provided");
            return BadRequest("No redirect_uri provided");
        }

        string authCode;
        
        if (existingSession != null)
        {
            // Reuse existing session
            authCode = await oidcService.GenerateAuthorizationCodeForExistingSessionAsync(
                session: existingSession,
                clientId: ClientId,
                redirectUri: RedirectUri,
                scopes: Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
                codeChallenge: CodeChallenge,
                codeChallengeMethod: CodeChallengeMethod,
                nonce: Nonce
            );
        }
        else
        {
            // Create new session (existing flow)
            authCode = await oidcService.GenerateAuthorizationCodeAsync(
                clientId: ClientId,
                userId: currentUser.Id,
                redirectUri: RedirectUri,
                scopes: Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
                codeChallenge: CodeChallenge,
                codeChallengeMethod: CodeChallengeMethod,
                nonce: Nonce
            );
        }

        // Build the redirect URI with the authorization code
        var redirectUriBuilder = new UriBuilder(RedirectUri);
        var query = System.Web.HttpUtility.ParseQueryString(redirectUriBuilder.Query);
        query["code"] = authCode;
        if (!string.IsNullOrEmpty(State))
            query["state"] = State;
        if (!string.IsNullOrEmpty(Scope))
            query["scope"] = Scope;
        redirectUriBuilder.Query = query.ToString();

        return Redirect(redirectUriBuilder.ToString());
    }

    public async Task<IActionResult> OnPostAsync(bool allow)
    {
        if (HttpContext.Items["CurrentUser"] is not Sphere.Account.Account currentUser) return Unauthorized();

        // First, validate the client ID
        if (string.IsNullOrEmpty(ClientIdString) || !Guid.TryParse(ClientIdString, out var clientId))
        {
            ModelState.AddModelError("client_id", "Invalid client_id format");
            return BadRequest("Invalid client_id format");
        }

        ClientId = clientId;

        // Check if a client exists
        var client = await oidcService.FindClientByIdAsync(ClientId);
        if (client == null)
        {
            ModelState.AddModelError("client_id", "Client not found");
            return NotFound("Client not found");
        }

        if (!allow)
        {
            // User denied the authorization request
            if (string.IsNullOrEmpty(RedirectUri))
                return BadRequest("No redirect_uri provided");

            var deniedUriBuilder = new UriBuilder(RedirectUri);
            var deniedQuery = System.Web.HttpUtility.ParseQueryString(deniedUriBuilder.Query);
            deniedQuery["error"] = "access_denied";
            deniedQuery["error_description"] = "The user denied the authorization request";
            if (!string.IsNullOrEmpty(State)) deniedQuery["state"] = State;
            deniedUriBuilder.Query = deniedQuery.ToString();

            return Redirect(deniedUriBuilder.ToString());
        }

        // User approved the request
        if (string.IsNullOrEmpty(RedirectUri))
        {
            ModelState.AddModelError("redirect_uri", "No redirect_uri provided");
            return BadRequest("No redirect_uri provided");
        }

        // Generate authorization code
        var authCode = await oidcService.GenerateAuthorizationCodeAsync(
            clientId: ClientId,
            userId: currentUser.Id,
            redirectUri: RedirectUri,
            scopes: Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
            codeChallenge: CodeChallenge,
            codeChallengeMethod: CodeChallengeMethod,
            nonce: Nonce);

        // Build the redirect URI with the authorization code
        var redirectUri = new UriBuilder(RedirectUri);
        var query = System.Web.HttpUtility.ParseQueryString(redirectUri.Query);

        // Add the authorization code
        query["code"] = authCode;

        // Add state if provided (for CSRF protection)
        if (!string.IsNullOrEmpty(State))
            query["state"] = State;

        // Set the query string
        redirectUri.Query = query.ToString();

        // Redirect back to the client with the authorization code
        return Redirect(redirectUri.ToString());
    }
}