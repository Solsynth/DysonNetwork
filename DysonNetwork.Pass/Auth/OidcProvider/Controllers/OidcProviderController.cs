using System.Security.Cryptography;
using DysonNetwork.Pass.Auth.OidcProvider.Responses;
using DysonNetwork.Pass.Auth.OidcProvider.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using System.Web;
using DysonNetwork.Pass.Auth.OidcProvider.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using NodaTime;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Auth.OidcProvider.Controllers;

[Route("/api/auth/open")]
[ApiController]
public class OidcProviderController(
    AppDatabase db,
    OidcProviderService oidcService,
    IConfiguration configuration,
    IOptions<OidcProviderOptions> options,
    ILogger<OidcProviderController> logger
) : ControllerBase
{
    [HttpGet("authorize")]
    [Produces("application/json")]
    public async Task<IActionResult> Authorize(
        [FromQuery(Name = "client_id")] string clientId,
        [FromQuery(Name = "response_type")] string responseType,
        [FromQuery(Name = "redirect_uri")] string? redirectUri = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? state = null,
        [FromQuery(Name = "response_mode")] string? responseMode = null,
        [FromQuery] string? nonce = null,
        [FromQuery] string? display = null,
        [FromQuery] string? prompt = null,
        [FromQuery(Name = "code_challenge")] string? codeChallenge = null,
        [FromQuery(Name = "code_challenge_method")]
        string? codeChallengeMethod = null)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "client_id is required"
            });
        }

        var client = await oidcService.FindClientBySlugAsync(clientId);
        if (client == null)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "unauthorized_client",
                ErrorDescription = "Client not found"
            });
        }

        // Validate response_type
        if (string.IsNullOrEmpty(responseType))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "response_type is required"
            });
        }

        // Check if the client is allowed to use the requested response type
        var allowedResponseTypes = new[] { "code", "token", "id_token" };
        var requestedResponseTypes = responseType.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (requestedResponseTypes.Any(rt => !allowedResponseTypes.Contains(rt)))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "unsupported_response_type",
                ErrorDescription = "The requested response type is not supported"
            });
        }

        // Validate redirect_uri if provided
        if (!string.IsNullOrEmpty(redirectUri) &&
            !await oidcService.ValidateRedirectUriAsync(Guid.Parse(client.Id), redirectUri))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "Invalid redirect_uri"
            });
        }

        // Return client information
        var clientInfo = new ClientInfoResponse
        {
            ClientId = Guid.Parse(client.Id),
            Picture = client.Picture is not null ? SnCloudFileReferenceObject.FromProtoValue(client.Picture) : null,
            Background = client.Background is not null
                ? SnCloudFileReferenceObject.FromProtoValue(client.Background)
                : null,
            ClientName = client.Name,
            HomeUri = client.Links.HomePage,
            PolicyUri = client.Links.PrivacyPolicy,
            TermsOfServiceUri = client.Links.TermsOfService,
            ResponseTypes = responseType,
            Scopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
            State = state,
            Nonce = nonce,
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod
        };

        return Ok(clientInfo);
    }

    [HttpPost("authorize")]
    [Consumes("application/x-www-form-urlencoded")]
    [Authorize]
    public async Task<IActionResult> HandleAuthorizationResponse(
        [FromForm(Name = "authorize")] string? authorize,
        [FromForm(Name = "client_id")] string clientId,
        [FromForm(Name = "redirect_uri")] string? redirectUri = null,
        [FromForm] string? scope = null,
        [FromForm] string? state = null,
        [FromForm] string? nonce = null,
        [FromForm(Name = "code_challenge")] string? codeChallenge = null,
        [FromForm(Name = "code_challenge_method")]
        string? codeChallengeMethod = null)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount account)
            return Unauthorized();

        // Find the client
        var client = await oidcService.FindClientBySlugAsync(clientId);
        if (client == null)
        {
            return BadRequest(new ErrorResponse
            {
                Error = "unauthorized_client",
                ErrorDescription = "Client not found"
            });
        }

        // If user denied the request
        if (string.IsNullOrEmpty(authorize) || !bool.TryParse(authorize, out var isAuthorized) || !isAuthorized)
        {
            var errorUri = new UriBuilder(redirectUri ?? client.Links?.HomePage ?? "https://example.com");
            var queryParams = HttpUtility.ParseQueryString(errorUri.Query);
            queryParams["error"] = "access_denied";
            queryParams["error_description"] = "The user denied the authorization request";
            if (!string.IsNullOrEmpty(state)) queryParams["state"] = state;

            errorUri.Query = queryParams.ToString();
            return Ok(new { redirectUri = errorUri.Uri.ToString() });
        }

        // Validate redirect_uri if provided
        if (!string.IsNullOrEmpty(redirectUri) &&
            !await oidcService.ValidateRedirectUriAsync(Guid.Parse(client!.Id), redirectUri))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "Invalid redirect_uri"
            });
        }

        // Default to client's first redirect URI if not provided
        redirectUri ??= client.OauthConfig?.RedirectUris?.FirstOrDefault();
        if (string.IsNullOrEmpty(redirectUri))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "invalid_request",
                ErrorDescription = "No valid redirect_uri available"
            });
        }

        try
        {
            // Generate authorization code and create session
            var authorizationCode = await oidcService.GenerateAuthorizationCodeAsync(
                Guid.Parse(client.Id),
                account.Id,
                redirectUri,
                scope?.Split(' ') ?? [],
                codeChallenge,
                codeChallengeMethod,
                nonce
            );

            // Build the redirect URI with the authorization code
            var redirectBuilder = new UriBuilder(redirectUri);
            var queryParams = HttpUtility.ParseQueryString(redirectBuilder.Query);
            queryParams["code"] = authorizationCode;
            if (!string.IsNullOrEmpty(state)) queryParams["state"] = state;

            redirectBuilder.Query = queryParams.ToString();

            return Ok(new { redirectUri = redirectBuilder.Uri.ToString() });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing authorization request");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Error = "server_error",
                ErrorDescription = "An error occurred while processing your request"
            });
        }
    }

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
                    var client = await oidcService.FindClientBySlugAsync(request.ClientId);
                    if (client == null ||
                        !await oidcService.ValidateClientCredentialsAsync(Guid.Parse(client.Id), request.ClientSecret))
                        return BadRequest(new ErrorResponse
                        { Error = "invalid_client", ErrorDescription = "Invalid client credentials" });

                    // Generate tokens
                    var tokenResponse = await oidcService.GenerateTokenResponseAsync(
                        clientId: Guid.Parse(client.Id),
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
                        if (session?.AppId is null || session.ExpiredAt < now)
                        {
                            return BadRequest(new ErrorResponse
                            {
                                Error = "invalid_grant",
                                ErrorDescription = "Invalid or expired refresh token"
                            });
                        }

                        // Get the client
                        var client = await oidcService.FindClientByIdAsync(session.AppId.Value);
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser ||
            HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession) return Unauthorized();

        // Get requested scopes from the token
        var scopes = currentSession.Scopes;

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
        var siteUrl = configuration["SiteUrl"];
        var issuer = options.Value.IssuerUri.TrimEnd('/');

        return Ok(new
        {
            issuer,
            authorization_endpoint = $"{siteUrl}/auth/authorize",
            token_endpoint = $"{baseUrl}/pass/auth/open/token",
            userinfo_endpoint = $"{baseUrl}/pass/auth/open/userinfo",
            jwks_uri = $"{baseUrl}/.well-known/jwks",
            scopes_supported = new[] { "openid", "profile", "email" },
            response_types_supported = new[]
                { "code", "token", "id_token", "code token", "code id_token", "token id_token", "code token id_token" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_basic", "client_secret_post" },
            id_token_signing_alg_values_supported = new[] { "HS256", "RS256" },
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
    public string? ClientId { get; set; }

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
