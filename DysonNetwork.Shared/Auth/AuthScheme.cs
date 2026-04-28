using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private const string ProfileCachePrefix = "auth:profile:";
    private static readonly TimeSpan SessionCacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan ProfileCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LastSeenTouchThrottle = TimeSpan.FromMinutes(1);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tokenInfo = ExtractToken(Request, config);
        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.Token))
        {
            Logger.LogDebug(
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
            var tokenScopes = TryExtractScopesFromJwt(tokenInfo.Token);

            // Extract session ID from JWT for cache key
            var sessionId = TryExtractSessionIdFromJwt(tokenInfo.Token);
            if (string.IsNullOrEmpty(sessionId))
            {
                // Fallback to token hash for non-JWT tokens (legacy/compact tokens)
                sessionId = GetTokenHash(tokenInfo.Token);
            }
            var cacheKey = AuthCacheKeys.Session(sessionId);

            // Check cache first
            var cachedSession = await cache.GetAsync<DyAuthSession>(cacheKey);
            if (cachedSession is not null)
            {
                ApplyTokenScopesIfPresent(cachedSession, tokenScopes);
                Logger.LogDebug("Auth cache hit for path={Path} sessionId={SessionId}", Request.Path, cachedSession.Id);
                await HydrateProfileAsync(cachedSession, Context.RequestAborted);
                await TouchProfileLastSeenAsync(cachedSession, Context.RequestAborted);
                return BuildAuthResult(tokenInfo.Type, cachedSession);
            }

            // Cache miss - validate via gRPC to Padlock
            Logger.LogDebug("Auth cache miss, validating via gRPC for path={Path}", Request.Path);

            DyAuthSession session;
            try
            {
                session = await ValidateViaGrpc(tokenInfo.Token, Request.HttpContext.GetClientIpAddress());
            }
            catch (InvalidOperationException ex)
            {
                Logger.LogInformation("Auth failed via gRPC. path={Path} reason={Reason}", Request.Path, ex.Message);
                return AuthenticateResult.Fail(ex.Message);
            }
            catch (RpcException ex)
            {
                Logger.LogInformation("Auth failed via gRPC rpc. path={Path} code={Code} detail={Detail}",
                    Request.Path, ex.Status.StatusCode, ex.Status.Detail);
                return AuthenticateResult.Fail($"Remote error: {ex.Status.StatusCode} - {ex.Status.Detail}");
            }

            // Cache the validated session using session ID
            var sessionCacheKey = AuthCacheKeys.Session(session.Id);
            await cache.SetAsync(sessionCacheKey, session, SessionCacheTtl);
            Logger.LogDebug("Auth session cached for 1h, sessionId={SessionId}", session.Id);

            ApplyTokenScopesIfPresent(session, tokenScopes);
            await HydrateProfileAsync(session, Context.RequestAborted);
            await TouchProfileLastSeenAsync(session, Context.RequestAborted);
            return BuildAuthResult(tokenInfo.Type, session);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Authentication failed unexpectedly. path={Path}", Request.Path);
            return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
        }
    }

    private static string? TryExtractSessionIdFromJwt(string token)
    {
        try
        {
            // JWT tokens have 3 parts separated by dots
            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            // Session ID can be in "jti" (JWT ID) or "sid" claim
            var sessionId = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value
                            ?? jwt.Claims.FirstOrDefault(c => c.Type == "sid")?.Value;

            return sessionId;
        }
        catch
        {
            // Not a valid JWT or can't be parsed
            return null;
        }
    }

    private static string GetTokenHash(string token)
    {
        // Use a simple hash for cache key - SHA256 for uniqueness
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static List<string> TryExtractScopesFromJwt(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
                return [];

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);
            var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var claim in jwt.Claims.Where(c => c.Type == "scope" || c.Type == "scp"))
            {
                if (string.IsNullOrWhiteSpace(claim.Value))
                    continue;

                foreach (var part in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!string.IsNullOrWhiteSpace(part))
                        scopes.Add(part);
                }
            }

            return scopes.ToList();
        }
        catch
        {
            return [];
        }
    }

    private static void ApplyTokenScopesIfPresent(DyAuthSession session, List<string> tokenScopes)
    {
        if (tokenScopes.Count == 0)
            return;

        session.Scopes.Clear();
        session.Scopes.Add(tokenScopes);
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

    private async Task TouchProfileLastSeenAsync(DyAuthSession session, CancellationToken cancellationToken)
    {
        if (session.Account is null || string.IsNullOrWhiteSpace(session.Account.Id))
            return;

        var throttleKey = $"auth:last_seen_touch:{session.Account.Id}";
        var (alreadyTouched, _) = await cache.GetAsyncWithStatus<bool>(throttleKey);
        if (alreadyTouched)
            return;

        try
        {
            var now = DateTime.UtcNow;
            await profiles.UpdateProfileAsync(
                new DyUpdateProfileRequest
                {
                    AccountId = session.Account.Id,
                    Profile = new DyAccountProfile
                    {
                        LastSeenAt = Timestamp.FromDateTime(now)
                    },
                    UpdateMask = new FieldMask
                    {
                        Paths = { "last_seen_at" }
                    }
                },
                cancellationToken: cancellationToken
            );
            await cache.SetAsync(throttleKey, true, LastSeenTouchThrottle);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.Unimplemented)
        {
            Logger.LogDebug("Profile last_seen touch unavailable for account {AccountId}: {StatusCode}",
                session.Account.Id, ex.StatusCode);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Failed to touch profile last_seen_at for account {AccountId}", session.Account.Id);
        }
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
