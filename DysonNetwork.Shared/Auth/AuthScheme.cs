using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DysonNetwork.Shared.Auth;

public class DysonTokenAuthOptions : AuthenticationSchemeOptions;

public class DysonTokenAuthHandler(
    IOptionsMonitor<DysonTokenAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DyAuthService.DyAuthServiceClient auth,
    DyProfileService.DyProfileServiceClient profiles,
    ICacheService cache,
    IConfiguration config
) : AuthenticationHandler<DysonTokenAuthOptions>(options, logger, encoder)
{
    private static readonly DateTime LegacyDefaultCutoffUtc = DateTime.UtcNow.AddDays(14);
    private const string RevokedJtiPrefix = "auth:revoked:jti:";
    private const string AccountVersionPrefix = "auth:account_ver:";
    private const string ProfileCachePrefix = "auth:profile:";
    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromMinutes(5);

    private readonly Lazy<RSA> _publicKey = new(() =>
    {
        var path = config["AuthToken:PublicKeyPath"] ??
                   throw new InvalidOperationException("AuthToken:PublicKeyPath is not configured.");
        var pem = File.ReadAllText(path);
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tokenInfo = ExtractToken(Request, config);
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.Token))
        {
            Logger.LogWarning(
                "Auth failed: no token extracted. path={Path} authHeaderPresent={AuthPresent} xForwardedAuthPresent={FwdPresent} xOriginalAuthPresent={OrigPresent}",
                Request.Path,
                Request.Headers.ContainsKey("Authorization"),
                Request.Headers.ContainsKey("X-Forwarded-Authorization"),
                Request.Headers.ContainsKey("X-Original-Authorization")
            );
            return AuthenticateResult.Fail("No token was provided.");
        }

        try
        {
            if (TryValidateJwtLocally(tokenInfo.Token, out var jwt, out var failMessage) && jwt != null)
            {
                var tokenUse = jwt.Claims.FirstOrDefault(c => c.Type == "token_use")?.Value;
                if (string.IsNullOrWhiteSpace(tokenUse))
                    tokenUse = tokenInfo.Type == TokenType.ApiKey ? "api_key" : "user";
                if (tokenUse == "refresh")
                    return AuthenticateResult.Fail("Refresh token cannot be used as bearer token.");

                if (tokenInfo.Type == TokenType.ApiKey && tokenUse != "api_key")
                    return AuthenticateResult.Fail("Bot auth scheme requires API key token.");

                if (tokenInfo.Type == TokenType.AuthKey && tokenUse == "api_key")
                    return AuthenticateResult.Fail("API key token must use Bot auth scheme.");

                var jti = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value;
                var accountId = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                                ?? jwt.Claims.FirstOrDefault(c => c.Type == "account_id")?.Value;
                var tokenVer = jwt.Claims.FirstOrDefault(c => c.Type == "ver")?.Value;

                if (string.IsNullOrWhiteSpace(jti) || string.IsNullOrWhiteSpace(accountId))
                    return AuthenticateResult.Fail("Missing required token claims.");

                if (await IsRevokedJti(jti))
                    return AuthenticateResult.Fail("Token has been revoked.");

                if (!await ValidateAccountVersion(accountId, tokenVer))
                    return AuthenticateResult.Fail("Token version is stale.");

                var session = BuildSessionFromClaims(jwt, tokenUse ?? "user");
                await HydrateProfileAsync(session, Context.RequestAborted);
                return BuildAuthResult(tokenInfo.Type, session);
            }

            if (!ShouldUseLegacyFallback(config))
            {
                Logger.LogWarning("Auth failed: local JWT validation failed and legacy fallback disabled. path={Path} reason={Reason}",
                    Request.Path, failMessage ?? "unknown");
                return AuthenticateResult.Fail(failMessage ?? "Token validation failed.");
            }

            // Compatibility path for old compact/legacy tokens during grace window.
            DyAuthSession legacySession;
            try
            {
                legacySession = await ValidateViaGrpc(tokenInfo.Token, Request.HttpContext.GetClientIpAddress());
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogWarning("Auth failed via gRPC fallback. path={Path} reason={Reason}", Request.Path, ex.Message);
                return AuthenticateResult.Fail(ex.Message);
            }
            catch (RpcException ex)
            {
                Logger.LogWarning("Auth failed via gRPC fallback rpc. path={Path} code={Code} detail={Detail}",
                    Request.Path, ex.Status.StatusCode, ex.Status.Detail);
                return AuthenticateResult.Fail($"Remote error: {ex.Status.StatusCode} - {ex.Status.Detail}");
            }

            await HydrateProfileAsync(legacySession, Context.RequestAborted);
            return BuildAuthResult(tokenInfo.Type, legacySession);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Authentication failed unexpectedly. path={Path}", Request.Path);
            return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
        }
    }

    private AuthenticateResult BuildAuthResult(TokenType tokenType, DyAuthSession session)
    {
        Context.Items["CurrentUser"] = session.Account;
        Context.Items["CurrentSession"] = session;
        Context.Items["CurrentTokenType"] = tokenType.ToString();

        var claims = new List<Claim>
        {
            new("user_id", session.Account.Id),
            new("session_id", session.Id),
            new("token_type", tokenType.ToString())
        };

        session.Scopes.ToList().ForEach(scope => claims.Add(new Claim("scope", scope)));
        if (session.Account.IsSuperuser) claims.Add(new Claim("is_superuser", "1"));

        var identity = new ClaimsIdentity(claims, AuthConstants.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthConstants.SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private DyAuthSession BuildSessionFromClaims(JwtSecurityToken jwt, string tokenUse)
    {
        var sessionId = jwt.Claims.FirstOrDefault(c => c.Type == "sid")?.Value
                        ?? jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value
                        ?? string.Empty;
        var accountId = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value
                        ?? jwt.Claims.FirstOrDefault(c => c.Type == "account_id")?.Value
                        ?? string.Empty;
        var name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? string.Empty;
        var nick = jwt.Claims.FirstOrDefault(c => c.Type == "nick")?.Value ?? string.Empty;
        var region = jwt.Claims.FirstOrDefault(c => c.Type == "region")?.Value ?? string.Empty;
        var isSuperuser = jwt.Claims.FirstOrDefault(c => c.Type == "is_superuser")?.Value == "1";
        var perkIdentifier = jwt.Claims.FirstOrDefault(c => c.Type == "perk_identifier")?.Value;
        var perkSubscriptionId = jwt.Claims.FirstOrDefault(c => c.Type == "perk_subscription_id")?.Value;
        var perkLevelRaw = jwt.Claims.FirstOrDefault(c => c.Type == "perk_level")?.Value;
        _ = int.TryParse(perkLevelRaw, out var perkLevel);

        var proto = new DyAuthSession
        {
            Id = sessionId,
            AccountId = accountId,
            Account = new DyAccount
            {
                Id = accountId,
                Name = name,
                Nick = nick,
                Region = region,
                IsSuperuser = isSuperuser,
                PerkLevel = perkLevel
            }
        };
        if (!string.IsNullOrWhiteSpace(perkIdentifier))
        {
            proto.Account.PerkSubscription = new DySubscriptionReferenceObject
            {
                Identifier = perkIdentifier,
                AccountId = accountId,
                Id = perkSubscriptionId ?? string.Empty
            };
        }
        proto.Scopes.AddRange(jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value));
        if (tokenUse == "api_key") proto.Type = DySessionType.DyLogin;

        return proto;
    }

    private bool TryValidateJwtLocally(string token, out JwtSecurityToken? jwt, out string? failMessage)
    {
        jwt = null;
        failMessage = null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var issuer = config["Authentication:Schemes:Bearer:ValidIssuer"] ?? "solar-network";
            var audience = config.GetSection("Authentication:Schemes:Bearer:ValidAudiences").Get<string[]>()?.FirstOrDefault()
                           ?? "solar-network";

            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new RsaSecurityKey(_publicKey.Value),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            };

            handler.ValidateToken(token, parameters, out var validatedToken);
            jwt = validatedToken as JwtSecurityToken;
            if (jwt == null)
            {
                failMessage = "Token is not a valid JWT.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            failMessage = ex.Message;
            return false;
        }
    }

    private async Task<DyAuthSession> ValidateViaGrpc(string token, string? ipAddress)
    {
        var resp = await auth.AuthenticateAsync(new DyAuthenticateRequest
        {
            Token = token,
            IpAddress = ipAddress
        });
        if (!resp.Valid) throw new InvalidOperationException(resp.Message);
        return resp.Session ?? throw new InvalidOperationException("Session not found.");
    }

    private async Task<bool> IsRevokedJti(string jti)
    {
        var (found, _) = await cache.GetAsyncWithStatus<bool>($"{RevokedJtiPrefix}{jti}");
        return found;
    }

    private async Task<bool> ValidateAccountVersion(string accountId, string? tokenVer)
    {
        if (!Guid.TryParse(accountId, out var parsedAccountId)) return false;
        var (found, currentVersion) = await cache.GetAsyncWithStatus<int>($"{AccountVersionPrefix}{parsedAccountId}");
        if (!found) return true;
        if (!int.TryParse(tokenVer, out var tokenVersion)) tokenVersion = 0;
        return tokenVersion >= currentVersion;
    }

    private async Task HydrateProfileAsync(DyAuthSession session, CancellationToken cancellationToken)
    {
        if (!config.GetValue("Auth:ProfileHydration:Enabled", true))
            return;

        if (session.Account is null || string.IsNullOrWhiteSpace(session.Account.Id))
            return;

        var cacheKey = $"{ProfileCachePrefix}{session.Account.Id}";
        var cached = await cache.GetAsync<DyAccountProfile>(cacheKey);
        if (cached is not null)
        {
            session.Account.Profile = cached;
            return;
        }

        try
        {
            var profile = await profiles.GetProfileAsync(
                new DyGetProfileRequest { AccountId = session.Account.Id },
                cancellationToken: cancellationToken
            );
            session.Account.Profile = profile;
            await cache.SetAsync(cacheKey, profile, ProfileCacheTtl);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.Unimplemented)
        {
            Logger.LogWarning("Profile lookup unavailable for account {AccountId}: {StatusCode}",
                session.Account.Id, ex.StatusCode);
            session.Account.Profile ??= new DyAccountProfile();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to hydrate account profile for {AccountId}", session.Account.Id);
            session.Account.Profile ??= new DyAccountProfile();
        }
    }

    private static bool ShouldUseLegacyFallback(IConfiguration config)
    {
        var useFallback = config.GetValue("Auth:Validation:UseGrpcFallbackForLegacy", true);
        if (!useFallback) return false;

        var acceptUntil = config["Auth:LegacyTokens:AcceptUntil"];
        if (DateTime.TryParse(acceptUntil, out var cutoff))
            return DateTime.UtcNow <= cutoff.ToUniversalTime();
        return DateTime.UtcNow <= LegacyDefaultCutoffUtc;
    }

    private static TokenInfo? ExtractToken(HttpRequest request, IConfiguration config)
    {
        if (request.Query.TryGetValue(AuthConstants.TokenQueryParamName, out var queryToken))
        {
            return new TokenInfo { Token = queryToken.ToString(), Type = TokenType.AuthKey };
        }

        var authHeader = NormalizeAuthHeader(ExtractRawAuthHeader(request));
        if (!string.IsNullOrEmpty(authHeader))
        {
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return new TokenInfo { Token = authHeader["Bearer ".Length..].Trim(), Type = TokenType.AuthKey };

            if (authHeader.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase))
                return new TokenInfo { Token = authHeader["Bot ".Length..].Trim(), Type = TokenType.ApiKey };

            var acceptLegacySchemes = config.GetValue("Auth:Headers:AcceptLegacySchemes", true) &&
                                      ShouldUseLegacyFallback(config);
            if (acceptLegacySchemes && authHeader.StartsWith("AtField ", StringComparison.OrdinalIgnoreCase))
                return new TokenInfo { Token = authHeader["AtField ".Length..].Trim(), Type = TokenType.AuthKey };
            if (acceptLegacySchemes && authHeader.StartsWith("AkField ", StringComparison.OrdinalIgnoreCase))
                return new TokenInfo { Token = authHeader["AkField ".Length..].Trim(), Type = TokenType.ApiKey };
        }

        if (request.Cookies.TryGetValue(AuthConstants.CookieTokenName, out var cookieToken))
            return new TokenInfo { Token = cookieToken, Type = TokenType.AuthKey };

        return null;
    }

    private static string ExtractRawAuthHeader(HttpRequest request)
    {
        // Standard header first.
        var authHeader = request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader)) return authHeader;

        // Common proxy-forwarded header variants.
        authHeader = request.Headers["X-Forwarded-Authorization"].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader)) return authHeader;

        authHeader = request.Headers["X-Original-Authorization"].ToString();
        if (!string.IsNullOrWhiteSpace(authHeader)) return authHeader;

        return string.Empty;
    }

    private static string NormalizeAuthHeader(string raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Some clients/proxies serialize header lists as "[Bearer xxx]".
        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
            value = value[1..^1].Trim();

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Trim();

        return value;
    }
}
