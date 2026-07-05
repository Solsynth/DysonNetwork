using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Web;
using DysonNetwork.Padlock.Auth.OidcProvider.Models;
using DysonNetwork.Padlock.Auth.OidcProvider.Options;
using DysonNetwork.Padlock.Auth.OidcProvider.Responses;
using DysonNetwork.Padlock.Auth.OidcProvider.Services;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NodaTime;

namespace DysonNetwork.Padlock.Auth.OidcProvider.Controllers;

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
    private async Task<SnCustomApp?> FindClientByIdentifierAsync(string clientIdentifier)
    {
        if (Guid.TryParse(clientIdentifier, out var clientId))
            return await oidcService.FindClientByIdAsync(clientId);
        return await oidcService.FindClientBySlugAsync(clientIdentifier);
    }

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
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod = null
    )
    {
        if (string.IsNullOrEmpty(clientId))
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "client_id is required",
                }
            );
        }

        var client = await FindClientByIdentifierAsync(clientId);
        if (client == null)
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "unauthorized_client",
                    ErrorDescription = "Client not found",
                }
            );
        }

        // Validate response_type
        if (string.IsNullOrEmpty(responseType))
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "response_type is required",
                }
            );
        }

        // Check if the client is allowed to use the requested response type
        var allowedResponseTypes = new[] { "code", "token", "id_token" };
        var requestedResponseTypes = responseType.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (requestedResponseTypes.Any(rt => !allowedResponseTypes.Contains(rt)))
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "unsupported_response_type",
                    ErrorDescription = "The requested response type is not supported",
                }
            );
        }

        // Validate redirect_uri if provided
        if (
            !string.IsNullOrEmpty(redirectUri)
            && !await oidcService.ValidateRedirectUriAsync(client.Id, redirectUri)
        )
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Invalid redirect_uri",
                }
            );
        }

        // Return client information
        var clientInfo = new ClientInfoResponse
        {
            ClientId = client.Slug,
            Picture = client.Picture,
            Background = client.Background,
            ClientName = client.Name,
            HomeUri = client.Links?.HomePage,
            PolicyUri = client.Links?.PrivacyPolicy,
            TermsOfServiceUri = client.Links?.TermsOfService,
            ResponseTypes = responseType,
            Scopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
            State = state,
            Nonce = nonce,
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = codeChallengeMethod,
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
        [FromForm(Name = "code_challenge_method")] string? codeChallengeMethod = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount account)
            return Unauthorized();

        // Find the client
        var client = await FindClientByIdentifierAsync(clientId);
        if (client == null)
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "unauthorized_client",
                    ErrorDescription = "Client not found",
                }
            );
        }

        // Public clients must use PKCE
        var isPublicClient = oidcService.IsPublicClient(client);
        if (isPublicClient && string.IsNullOrEmpty(codeChallenge))
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription =
                        "PKCE is required for public clients. Please provide code_challenge.",
                }
            );
        }

        // If user denied the request
        if (
            string.IsNullOrEmpty(authorize)
            || !bool.TryParse(authorize, out var isAuthorized)
            || !isAuthorized
        )
        {
            var errorUri = new UriBuilder(
                redirectUri ?? client.Links?.HomePage ?? "https://example.com"
            );
            var queryParams = HttpUtility.ParseQueryString(errorUri.Query);
            queryParams["error"] = "access_denied";
            queryParams["error_description"] = "The user denied the authorization request";
            if (!string.IsNullOrEmpty(state))
                queryParams["state"] = state;

            errorUri.Query = queryParams.ToString();
            return Ok(new { redirectUri = errorUri.Uri.ToString() });
        }

        // Validate redirect_uri if provided
        if (
            !string.IsNullOrEmpty(redirectUri)
            && !await oidcService.ValidateRedirectUriAsync(client!.Id, redirectUri)
        )
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Invalid redirect_uri",
                }
            );
        }

        // Default to client's first redirect URI if not provided
        redirectUri ??= client.OauthConfig?.RedirectUris?.FirstOrDefault();
        if (string.IsNullOrEmpty(redirectUri))
        {
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "No valid redirect_uri available",
                }
            );
        }

        try
        {
            // Generate authorization code and create session
            var authorizationCode = await oidcService.GenerateAuthorizationCodeAsync(
                client.Id,
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
            if (!string.IsNullOrEmpty(state))
                queryParams["state"] = state;

            redirectBuilder.Query = queryParams.ToString();

            return Ok(new { redirectUri = redirectBuilder.Uri.ToString() });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing authorization request");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new ErrorResponse
                {
                    Error = "server_error",
                    ErrorDescription = "An error occurred while processing your request",
                }
            );
        }
    }

    [HttpPost("token")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token([FromForm] TokenRequest request)
    {
        if (request.ClientId == null)
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "client_id is required",
                }
            );

        var client = await FindClientByIdentifierAsync(request.ClientId);
        if (client == null)
            return BadRequest(
                new ErrorResponse
                {
                    Error = "unauthorized_client",
                    ErrorDescription = "Client not found",
                }
            );

        var isPublicClient = oidcService.IsPublicClient(client);

        switch (request.GrantType)
        {
            case "authorization_code" when request.Code == null:
                return BadRequest(
                    new ErrorResponse
                    {
                        Error = "invalid_request",
                        ErrorDescription = "Authorization code is required",
                    }
                );
            case "authorization_code":
            {
                if (!isPublicClient)
                {
                    if (
                        string.IsNullOrEmpty(request.ClientSecret)
                        || !await oidcService.ValidateClientCredentialsAsync(
                            client.Id,
                            request.ClientSecret
                        )
                    )
                        return BadRequest(
                            new ErrorResponse
                            {
                                Error = "invalid_client",
                                ErrorDescription = "Invalid client credentials",
                            }
                        );
                }

                var tokenResponse = await oidcService.GenerateTokenResponseAsync(
                    clientId: client.Id,
                    authorizationCode: request.Code!,
                    redirectUri: request.RedirectUri,
                    codeVerifier: request.CodeVerifier,
                    isPublicClient: isPublicClient
                );

                return Ok(tokenResponse);
            }
            case "refresh_token" when string.IsNullOrEmpty(request.RefreshToken):
                return BadRequest(
                    new ErrorResponse
                    {
                        Error = "invalid_request",
                        ErrorDescription = "Refresh token is required",
                    }
                );
            case "refresh_token":
            {
                if (!isPublicClient)
                {
                    if (
                        string.IsNullOrEmpty(request.ClientSecret)
                        || !await oidcService.ValidateClientCredentialsAsync(
                            client.Id,
                            request.ClientSecret!
                        )
                    )
                        return BadRequest(
                            new ErrorResponse
                            {
                                Error = "invalid_client",
                                ErrorDescription = "Invalid client credentials",
                            }
                        );
                }

                try
                {
                    var tokenResponse = await oidcService.GenerateTokenResponseAsync(
                        clientId: client.Id,
                        refreshToken: request.RefreshToken
                    );

                    return Ok(tokenResponse);
                }
                catch (InvalidOperationException ex)
                {
                    logger.LogWarning(
                        ex,
                        "OIDC refresh token grant failed for client {ClientId}",
                        request.ClientId
                    );
                    return BadRequest(
                        new ErrorResponse
                        {
                            Error = "invalid_grant",
                            ErrorDescription = "Invalid or expired refresh token",
                        }
                    );
                }
            }
            case "urn:ietf:params:oauth:grant-type:device_code"
                when string.IsNullOrEmpty(request.DeviceCode):
                return BadRequest(
                    new ErrorResponse
                    {
                        Error = "invalid_request",
                        ErrorDescription = "device_code is required",
                    }
                );
            case "urn:ietf:params:oauth:grant-type:device_code":
            {
                if (!isPublicClient)
                {
                    if (
                        string.IsNullOrEmpty(request.ClientSecret)
                        || !await oidcService.ValidateClientCredentialsAsync(
                            client.Id,
                            request.ClientSecret!
                        )
                    )
                        return BadRequest(
                            new ErrorResponse
                            {
                                Error = "invalid_client",
                                ErrorDescription = "Invalid client credentials",
                            }
                        );
                }

                try
                {
                    var tokenResponse = await oidcService.HandleDeviceCodeGrantAsync(
                        request.DeviceCode!,
                        client.Id
                    );
                    return Ok(tokenResponse);
                }
                catch (InvalidOperationException ex)
                {
                    var errorCode = ex.Message switch
                    {
                        var m when m.Contains("pending") => "authorization_pending",
                        var m when m.Contains("expired") => "expired_token",
                        var m when m.Contains("declined") => "access_denied",
                        _ => "invalid_grant",
                    };

                    return BadRequest(
                        new ErrorResponse { Error = errorCode, ErrorDescription = ex.Message }
                    );
                }
            }
            default:
                return BadRequest(new ErrorResponse { Error = "unsupported_grant_type" });
        }
    }

    [HttpGet("userinfo")]
    public async Task<IActionResult> GetUserInfo()
    {
        var bearer = Request.Headers.Authorization.ToString();
        if (
            string.IsNullOrWhiteSpace(bearer)
            || !bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        )
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return Unauthorized();
        }

        var token = bearer["Bearer ".Length..].Trim();
        var (isValid, jwt) = oidcService.ValidateToken(token);
        if (!isValid || jwt is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        var accountIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        var sessionIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
        if (
            !Guid.TryParse(accountIdClaim, out var accountId)
            || !Guid.TryParse(sessionIdClaim, out var sessionId)
        )
        {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        var currentSession = await oidcService.FindSessionByIdAsync(sessionId);
        if (currentSession == null || currentSession.AccountId != accountId)
        {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        var currentUser = currentSession.Account;
        if (currentUser == null)
        {
            Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
            return Unauthorized();
        }

        // Get requested scopes from the token
        var scopes = jwt
            .Claims.Where(c => c.Type == "scope")
            .Select(c => c.Value)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var userInfo = new Dictionary<string, object> { ["sub"] = currentUser.Id };

        // Include standard claims based on scopes
        if (scopes.Contains("profile") || scopes.Contains("name"))
        {
            userInfo["name"] = currentUser.Name;
            userInfo["preferred_username"] = currentUser.Nick;
        }

        var userEmail = await db
            .AccountContacts.Where(c =>
                c.Type == AccountContactType.Email && c.AccountId == currentUser.Id
            )
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

        return Ok(
            new
            {
                issuer,
                authorization_endpoint = $"{siteUrl}/auth/authorize",
                device_authorization_endpoint = $"{baseUrl}/padlock/auth/open/device/code",
                token_endpoint = $"{baseUrl}/padlock/auth/open/token",
                userinfo_endpoint = $"{baseUrl}/padlock/auth/open/userinfo",
                jwks_uri = $"{baseUrl}/.well-known/jwks",
                scopes_supported = new[] { "openid", "profile", "email" },
                response_types_supported = new[]
                {
                    "code",
                    "token",
                    "id_token",
                    "code token",
                    "code id_token",
                    "token id_token",
                    "code token id_token",
                },
                grant_types_supported = new[]
                {
                    "authorization_code",
                    "refresh_token",
                    "urn:ietf:params:oauth:grant-type:device_code",
                },
                token_endpoint_auth_methods_supported = new[]
                {
                    "client_secret_basic",
                    "client_secret_post",
                    "none",
                },
                id_token_signing_alg_values_supported = new[] { "HS256", "RS256" },
                subject_types_supported = new[] { "public" },
                claims_supported = new[] { "sub", "name", "email", "email_verified" },
                code_challenge_methods_supported = new[] { "S256" },
                response_modes_supported = new[] { "query", "fragment", "form_post" },
                request_parameter_supported = true,
                request_uri_parameter_supported = true,
                require_request_uri_registration = false,
            }
        );
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
        var keyId = Convert
            .ToBase64String(SHA256.HashData(parameters.Modulus!)[..8])
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");

        return Ok(
            new
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
                        alg = "RS256",
                    },
                },
            }
        );
    }

    public class DeviceCodeRequest
    {
        [Required]
        [JsonPropertyName("client_id")]
        [FromForm(Name = "client_id")]
        public string ClientId { get; set; } = null!;

        [JsonPropertyName("scope")]
        [FromForm(Name = "scope")]
        public string? Scope { get; set; }

        [JsonPropertyName("nonce")]
        [FromForm(Name = "nonce")]
        public string? Nonce { get; set; }
    }

    [HttpPost("device/code")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> RequestDeviceCode([FromForm] DeviceCodeRequest request)
    {
        if (string.IsNullOrEmpty(request.ClientId))
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "client_id is required",
                }
            );

        var client = await FindClientByIdentifierAsync(request.ClientId);
        if (client == null)
            return BadRequest(
                new ErrorResponse
                {
                    Error = "unauthorized_client",
                    ErrorDescription = "Client not found",
                }
            );

        var siteUrl = configuration["SiteUrl"] ?? "https://solsynth.dev";
        var scopes = request.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

        var info = await oidcService.GenerateDeviceCodeAsync(client.Id, scopes, request.Nonce);

        return Ok(
            new DeviceCodeResponse
            {
                DeviceCode = info.DeviceCode,
                UserCode = info.UserCode,
                VerificationUri = $"{siteUrl}/auth/device",
                VerificationUriComplete = $"{siteUrl}/auth/device?code={info.UserCode}",
                ExpiresIn = (int)(info.ExpiresAt - info.CreatedAt).TotalSeconds,
                Interval = 5,
            }
        );
    }

    public class DeviceCodeStatusResponse
    {
        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonPropertyName("client_id")]
        public Guid ClientId { get; set; }

        [JsonPropertyName("scopes")]
        public List<string> Scopes { get; set; } = [];

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public Instant ExpiresAt { get; set; }
    }

    [HttpGet("device/code/{userCode}")]
    public async Task<IActionResult> GetDeviceCodeStatus(string userCode)
    {
        var info = await oidcService.GetDeviceCodeByUserCodeAsync(userCode.ToUpperInvariant());
        if (info == null)
            return NotFound(
                new ErrorResponse
                {
                    Error = "not_found",
                    ErrorDescription = "Device code not found or expired.",
                }
            );

        return Ok(
            new DeviceCodeStatusResponse
            {
                UserCode = info.UserCode,
                ClientId = info.ClientId,
                Scopes = info.Scopes,
                Status = info.Status.ToString().ToLowerInvariant(),
                ExpiresAt = info.ExpiresAt,
            }
        );
    }

    [Authorize]
    [RequireInteractiveSession]
    [HttpPost("device/code/{userCode}/approve")]
    public async Task<IActionResult> ApproveDeviceCode(string userCode)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        if (HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var info = await oidcService.GetDeviceCodeByUserCodeAsync(userCode.ToUpperInvariant());
        if (info == null)
            return NotFound(
                new ErrorResponse
                {
                    Error = "not_found",
                    ErrorDescription = "Device code not found or expired.",
                }
            );

        if (info.Status != DeviceCodeStatus.Pending)
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Device code is no longer pending.",
                }
            );

        var now = SystemClock.Instance.GetCurrentInstant();
        if (now > info.ExpiresAt)
        {
            info.Status = DeviceCodeStatus.Expired;
            await oidcService.UpdateDeviceCodeAsync(info);
            return BadRequest(
                new ErrorResponse
                {
                    Error = "expired_token",
                    ErrorDescription = "Device code has expired.",
                }
            );
        }

        info.AccountId = currentUser.Id;
        info.Status = DeviceCodeStatus.Approved;
        info.ApprovedAt = now;
        info.ApprovedBySessionId = currentSession.Id;
        await oidcService.UpdateDeviceCodeAsync(info);

        return Ok();
    }

    [Authorize]
    [RequireInteractiveSession]
    [HttpPost("device/code/{userCode}/decline")]
    public async Task<IActionResult> DeclineDeviceCode(string userCode)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();
        if (HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized();

        var info = await oidcService.GetDeviceCodeByUserCodeAsync(userCode.ToUpperInvariant());
        if (info == null)
            return NotFound(
                new ErrorResponse
                {
                    Error = "not_found",
                    ErrorDescription = "Device code not found or expired.",
                }
            );

        if (info.Status != DeviceCodeStatus.Pending)
            return BadRequest(
                new ErrorResponse
                {
                    Error = "invalid_request",
                    ErrorDescription = "Device code is no longer pending.",
                }
            );

        info.Status = DeviceCodeStatus.Declined;
        info.ApprovedAt = SystemClock.Instance.GetCurrentInstant();
        info.ApprovedBySessionId = currentSession.Id;
        await oidcService.UpdateDeviceCodeAsync(info);

        return Ok();
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

    [JsonPropertyName("device_code")]
    [FromForm(Name = "device_code")]
    public string? DeviceCode { get; set; }
}
