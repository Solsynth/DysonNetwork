using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Sphere.Developer;
using DysonNetwork.Sphere.Auth.OidcProvider.Options;
using DysonNetwork.Sphere.Auth.OidcProvider.Responses;
using DysonNetwork.Sphere.Auth.OidcProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace DysonNetwork.Sphere.Auth.OidcProvider.Controllers;

[Route("connect")]
[ApiController]
public class OidcProviderController(
    OidcProviderService oidcService,
    IOptions<OidcProviderOptions> options,
    ILogger<OidcProviderController> logger
)
    : ControllerBase
{
    [HttpGet("authorize")]
    public async Task<IActionResult> Authorize(
        [Required][FromQuery(Name = "client_id")] Guid clientId,
        [Required][FromQuery(Name = "response_type")] string responseType,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery] string? scope,
        [FromQuery] string? state,
        [FromQuery] string? nonce,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery(Name = "response_mode")] string? responseMode
    )
    {
        // Check if user is authenticated
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
        {
            // Not authenticated - redirect to login with return URL
            var loginUrl = "/Auth/Login";
            var returnUrl = $"{Request.Path}{Request.QueryString}";
            return Redirect($"{loginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }
        
        // Validate client
        var client = await oidcService.FindClientByIdAsync(clientId);
        if (client == null)
            return BadRequest(new ErrorResponse { Error = "invalid_client", ErrorDescription = "Client not found" });
            
        // Check if user has already granted permission to this client
        // For now, we'll always show the consent page. In a real app, you might store consent decisions.
        // If you want to implement "remember my decision", you would check that here.
        var consentRequired = true;
        
        if (consentRequired)
        {
            // Redirect to consent page with all the OAuth parameters
            var consentUrl = $"/Auth/Authorize?client_id={clientId}";
            if (!string.IsNullOrEmpty(responseType)) consentUrl += $"&response_type={Uri.EscapeDataString(responseType)}";
            if (!string.IsNullOrEmpty(redirectUri)) consentUrl += $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";
            if (!string.IsNullOrEmpty(scope)) consentUrl += $"&scope={Uri.EscapeDataString(scope)}";
            if (!string.IsNullOrEmpty(state)) consentUrl += $"&state={Uri.EscapeDataString(state)}";
            if (!string.IsNullOrEmpty(nonce)) consentUrl += $"&nonce={Uri.EscapeDataString(nonce)}";
            if (!string.IsNullOrEmpty(codeChallenge)) consentUrl += $"&code_challenge={Uri.EscapeDataString(codeChallenge)}";
            if (!string.IsNullOrEmpty(codeChallengeMethod)) consentUrl += $"&code_challenge_method={Uri.EscapeDataString(codeChallengeMethod)}";
            if (!string.IsNullOrEmpty(responseMode)) consentUrl += $"&response_mode={Uri.EscapeDataString(responseMode)}";
            
            return Redirect(consentUrl);
        }

        // Skip redirect_uri validation for apps in Developing status
        if (client.Status != CustomAppStatus.Developing)
        {
            // Validate redirect URI for non-Developing apps
            if (!string.IsNullOrEmpty(redirectUri) && !(client.RedirectUris?.Contains(redirectUri) ?? false))
                return BadRequest(
                    new ErrorResponse { Error = "invalid_request", ErrorDescription = "Invalid redirect_uri" });
        }
        else
        {
            logger.LogWarning("Skipping redirect_uri validation for app {AppId} in Developing status", clientId);

            // If no redirect_uri is provided and we're in development, use the first one
            if (string.IsNullOrEmpty(redirectUri) && client.RedirectUris?.Any() == true)
            {
                redirectUri = client.RedirectUris.First();
            }
        }

        // Generate authorization code
        var code = Guid.NewGuid().ToString("N");
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        // In a real implementation, you'd store this code with the user's consent and requested scopes
        // and validate it in the token endpoint

        // For now, we'll just return the code directly (simplified for example)
        var response = new AuthorizationResponse
        {
            Code = code,
            State = state,
            Scope = scope,
            Issuer = options.Value.IssuerUri
        };

        // Redirect back to the client with the authorization code
        var finalRedirectUri = new UriBuilder(redirectUri ?? client.RedirectUris?.First() ?? throw new InvalidOperationException("No redirect URI provided and no default redirect URI found"));
        var query = System.Web.HttpUtility.ParseQueryString(finalRedirectUri.Query);
        query["code"] = response.Code;
        if (!string.IsNullOrEmpty(response.State))
            query["state"] = response.State;
        if (!string.IsNullOrEmpty(response.Scope))
            query["scope"] = response.Scope;

        finalRedirectUri.Query = query.ToString();
        return Redirect(finalRedirectUri.Uri.ToString());
    }

    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request)
    {
        if (request.GrantType == "authorization_code")
        {
            // Validate client credentials
            if (request.ClientId == null || string.IsNullOrEmpty(request.ClientSecret))
                return BadRequest(new ErrorResponse { Error = "invalid_client", ErrorDescription = "Client credentials are required" });

            var client = await oidcService.FindClientByIdAsync(request.ClientId.Value);
            if (client == null || !await oidcService.ValidateClientCredentialsAsync(request.ClientId.Value, request.ClientSecret))
                return BadRequest(new ErrorResponse { Error = "invalid_client", ErrorDescription = "Invalid client credentials" });

            // Validate the authorization code
            var authCode = await oidcService.ValidateAuthorizationCodeAsync(
                request.Code ?? string.Empty,
                request.ClientId.Value,
                request.RedirectUri,
                request.CodeVerifier);
                
            if (authCode == null)
            {
                logger.LogWarning("Invalid or expired authorization code: {Code}", request.Code);
                return BadRequest(new ErrorResponse { Error = "invalid_grant", ErrorDescription = "Invalid or expired authorization code" });
            }
            
            // Generate tokens
            var tokenResponse = await oidcService.GenerateTokenResponseAsync(
                clientId: request.ClientId.Value,
                subjectId: authCode.UserId,
                scopes: authCode.Scopes,
                authorizationCode: request.Code);

            return Ok(tokenResponse);
        }
        else if (request.GrantType == "refresh_token")
        {
            // Handle refresh token request
            // In a real implementation, you would validate the refresh token
            // and issue a new access token
            return BadRequest(new ErrorResponse { Error = "unsupported_grant_type" });
        }

        return BadRequest(new ErrorResponse { Error = "unsupported_grant_type" });
    }

    [HttpGet("userinfo")]
    [Authorize(AuthenticationSchemes = "Bearer")]
    public async Task<IActionResult> UserInfo()
    {
        var authHeader = HttpContext.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            var loginUrl = "/Account/Login"; // Update this path to your actual login page path
            var returnUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
            return Redirect($"{loginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        var token = authHeader["Bearer ".Length..].Trim();
        var jwtToken = oidcService.ValidateToken(token);

        if (jwtToken == null)
        {
            var loginUrl = "/Account/Login"; // Update this path to your actual login page path
            var returnUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
            return Redirect($"{loginUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        // Get user info based on the subject claim from the token
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var userName = User.FindFirstValue(ClaimTypes.Name);
        var userEmail = User.FindFirstValue(ClaimTypes.Email);

        // Get requested scopes from the token
        var scopes = jwtToken.Claims
            .Where(c => c.Type == "scope")
            .SelectMany(c => c.Value.Split(' '))
            .ToHashSet();

        var userInfo = new Dictionary<string, object>
        {
            ["sub"] = userId ?? "anonymous"
        };

        // Include standard claims based on scopes
        if (scopes.Contains("profile") || scopes.Contains("name"))
        {
            if (!string.IsNullOrEmpty(userName))
                userInfo["name"] = userName;
        }

        if (scopes.Contains("email") && !string.IsNullOrEmpty(userEmail))
        {
            userInfo["email"] = userEmail;
            userInfo["email_verified"] = true; // In a real app, check if email is verified
        }

        return Ok(userInfo);
    }

    [HttpGet(".well-known/openid-configuration")]
    public IActionResult GetConfiguration()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}".TrimEnd('/');
        var issuer = options.Value.IssuerUri.TrimEnd('/');

        return Ok(new
        {
            issuer = issuer,
            authorization_endpoint = $"{baseUrl}/connect/authorize",
            token_endpoint = $"{baseUrl}/connect/token",
            userinfo_endpoint = $"{baseUrl}/connect/userinfo",
            jwks_uri = $"{baseUrl}/.well-known/openid-configuration/jwks",
            scopes_supported = new[] { "openid", "profile", "email" },
            response_types_supported = new[] { "code", "token", "id_token", "code token", "code id_token", "token id_token", "code token id_token" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post" },
            id_token_signing_alg_values_supported = new[] { "HS256" },
            subject_types_supported = new[] { "public" },
            claims_supported = new[] { "sub", "name", "email", "email_verified" },
            code_challenge_methods_supported = new[] { "S256" },
            response_modes_supported = new[] { "query", "fragment", "form_post" },
            request_parameter_supported = true,
            request_uri_parameter_supported = true,
            require_request_uri_registration = false
        });
    }

    [HttpGet("jwks")]
    public IActionResult Jwks()
    {
        // In a production environment, you should use asymmetric keys (RSA or EC)
        // and expose only the public key here. This is a simplified example using HMAC.
        // For production, consider using RSA or EC keys and proper key rotation.

        var keyBytes = Encoding.UTF8.GetBytes(options.Value.SigningKey);
        var keyId = Convert.ToBase64String(SHA256.HashData(keyBytes)[..8])
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");

        return Ok(new
        {
            keys = new[]
            {
                new
                {
                    kty = "oct",
                    use = "sig",
                    kid = keyId,
                    k = Convert.ToBase64String(keyBytes),
                    alg = "HS256"
                }
            }
        });
    }
}

public class TokenRequest
{
    [JsonPropertyName("grant_type")]
    public string? GrantType { get; set; }
    
    [JsonPropertyName("code")]
    public string? Code { get; set; }
    
    [JsonPropertyName("redirect_uri")]
    public string? RedirectUri { get; set; }
    
    [JsonPropertyName("client_id")]
    public Guid? ClientId { get; set; }
    
    [JsonPropertyName("client_secret")]
    public string? ClientSecret { get; set; }
    
    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }
    
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
    
    [JsonPropertyName("code_verifier")]
    public string? CodeVerifier { get; set; }
}