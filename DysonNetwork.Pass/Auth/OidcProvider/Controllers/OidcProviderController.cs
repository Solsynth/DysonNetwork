using System.Security.Cryptography;
using DysonNetwork.Pass.Auth.OidcProvider.Responses;
using DysonNetwork.Pass.Auth.OidcProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Auth.OidcProvider.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Pass.Auth.OidcProvider.Controllers;

[Route("/api/auth/open")]
[ApiController]
public class OidcProviderController(
    AppDatabase db,
    OidcProviderService oidcService,
    IConfiguration configuration,
    IOptions<OidcProviderOptions> options,
    ILogger<OidcProviderController> logger
)
    : ControllerBase
{
    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request)
    {
        switch (request.GrantType)
        {
            // Validate client credentials
            case "authorization_code" when request.ClientId == null || string.IsNullOrEmpty(request.ClientSecret):
                return BadRequest("Client credentials are required");
            case "authorization_code" when request.Code == null:
                return BadRequest("Authorization code is required");
            case "authorization_code":
            {
                var client = await oidcService.FindClientByIdAsync(request.ClientId.Value);
                if (client == null ||
                    !await oidcService.ValidateClientCredentialsAsync(request.ClientId.Value, request.ClientSecret))
                    return BadRequest(new ErrorResponse
                        { Error = "invalid_client", ErrorDescription = "Invalid client credentials" });

                // Generate tokens
                var tokenResponse = await oidcService.GenerateTokenResponseAsync(
                    clientId: request.ClientId.Value,
                    authorizationCode: request.Code!,
                    redirectUri: request.RedirectUri,
                    codeVerifier: request.CodeVerifier
                );

                return Ok(tokenResponse);
            }
            case "refresh_token" when string.IsNullOrEmpty(request.RefreshToken):
                return BadRequest(new ErrorResponse
                    { Error = "invalid_request", ErrorDescription = "Refresh token is required" });
            case "refresh_token":
            {
                try
                {
                    // Decode the base64 refresh token to get the session ID
                    var sessionIdBytes = Convert.FromBase64String(request.RefreshToken);
                    var sessionId = new Guid(sessionIdBytes);

                    // Find the session and related data
                    var session = await oidcService.FindSessionByIdAsync(sessionId);
                    var now = SystemClock.Instance.GetCurrentInstant();
                    if (session?.App is null || session.ExpiredAt < now)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Error = "invalid_grant",
                            ErrorDescription = "Invalid or expired refresh token"
                        });
                    }

                    // Get the client
                    var client = session.App;
                    if (client == null)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Error = "invalid_client",
                            ErrorDescription = "Client not found"
                        });
                    }

                    // Generate new tokens
                    var tokenResponse = await oidcService.GenerateTokenResponseAsync(
                        clientId: session.AppId!.Value,
                        sessionId: session.Id
                    );

                    return Ok(tokenResponse);
                }
                catch (FormatException)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Error = "invalid_grant",
                        ErrorDescription = "Invalid refresh token format"
                    });
                }
            }
            default:
                return BadRequest(new ErrorResponse { Error = "unsupported_grant_type" });
        }
    }

    [HttpGet("userinfo")]
    [Authorize]
    public async Task<IActionResult> GetUserInfo()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser ||
            HttpContext.Items["CurrentSession"] is not AuthSession currentSession) return Unauthorized();

        // Get requested scopes from the token
        var scopes = currentSession.Challenge.Scopes;

        var userInfo = new Dictionary<string, object>
        {
            ["sub"] = currentUser.Id
        };

        // Include standard claims based on scopes
        if (scopes.Contains("profile") || scopes.Contains("name"))
        {
            userInfo["name"] = currentUser.Name;
            userInfo["preferred_username"] = currentUser.Nick;
        }

        var userEmail = await db.AccountContacts
            .Where(c => c.Type == AccountContactType.Email && c.AccountId == currentUser.Id)
            .FirstOrDefaultAsync();
        if (scopes.Contains("email") && userEmail is not null)
        {
            userInfo["email"] = userEmail.Content;
            userInfo["email_verified"] = userEmail.VerifiedAt is not null;
        }

        return Ok(userInfo);
    }

    [HttpGet("/.well-known/openid-configuration")]
    public IActionResult GetConfiguration()
    {
        var baseUrl = configuration["BaseUrl"];
        var issuer = options.Value.IssuerUri.TrimEnd('/');

        return Ok(new
        {
            issuer = issuer,
            authorization_endpoint = $"{baseUrl}/auth/authorize",
            token_endpoint = $"{baseUrl}/auth/open/token",
            userinfo_endpoint = $"{baseUrl}/auth/open/userinfo",
            jwks_uri = $"{baseUrl}/.well-known/jwks",
            scopes_supported = new[] { "openid", "profile", "email" },
            response_types_supported = new[]
                { "code", "token", "id_token", "code token", "code id_token", "token id_token", "code token id_token" },
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

    [HttpGet("/.well-known/jwks")]
    public IActionResult GetJwks()
    {
        using var rsa = options.Value.GetRsaPublicKey();
        if (rsa == null)
        {
            return BadRequest("Public key is not configured");
        }

        var parameters = rsa.ExportParameters(false);
        var keyId = Convert.ToBase64String(SHA256.HashData(parameters.Modulus!)[..8])
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");

        return Ok(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = keyId,
                    n = Base64UrlEncoder.Encode(parameters.Modulus!),
                    e = Base64UrlEncoder.Encode(parameters.Exponent!),
                    alg = "RS256"
                }
            }
        });
    }
}

public class TokenRequest
{
    [JsonPropertyName("grant_type")]
    [FromForm(Name = "grant_type")]
    public string? GrantType { get; set; }

    [JsonPropertyName("code")]
    [FromForm(Name = "code")]
    public string? Code { get; set; }

    [JsonPropertyName("redirect_uri")]
    [FromForm(Name = "redirect_uri")]
    public string? RedirectUri { get; set; }

    [JsonPropertyName("client_id")]
    [FromForm(Name = "client_id")]
    public Guid? ClientId { get; set; }

    [JsonPropertyName("client_secret")]
    [FromForm(Name = "client_secret")]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("refresh_token")]
    [FromForm(Name = "refresh_token")]
    public string? RefreshToken { get; set; }

    [JsonPropertyName("scope")]
    [FromForm(Name = "scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("code_verifier")]
    [FromForm(Name = "code_verifier")]
    public string? CodeVerifier { get; set; }
}