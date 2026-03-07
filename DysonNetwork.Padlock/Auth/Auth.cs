using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using DysonNetwork.Padlock.Handlers;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Extensions;
using SystemClock = NodaTime.SystemClock;

namespace DysonNetwork.Padlock.Auth;

public static class AuthConstants
{
    public const string SchemeName = "DysonToken";
    public const string TokenQueryParamName = "tk";
    public const string CookieTokenName = "AuthToken";
    public const string RefreshCookieTokenName = "RefreshToken";
}

public enum TokenType
{
    AuthKey,
    ApiKey,
    OidcKey,
    Unknown
}

public class TokenInfo
{
    public string Token { get; set; } = string.Empty;
    public TokenType Type { get; set; } = TokenType.Unknown;
}

public class DysonTokenAuthOptions : AuthenticationSchemeOptions;

public class DysonTokenAuthHandler(
    IOptionsMonitor<DysonTokenAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    TokenAuthService token,
    FlushBufferService fbs,
    IConfiguration config
)
    : AuthenticationHandler<DysonTokenAuthOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tokenInfo = _ExtractToken(Request);

        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.Token))
            return AuthenticateResult.Fail("No token was provided.");

        try
        {
            var ipAddress = Context.GetClientIpAddress();
            
            var (valid, session, message, tokenUse) = await token.AuthenticateTokenAsync(tokenInfo.Token, ipAddress);
            if (!valid || session is null)
                return AuthenticateResult.Fail(message ?? "Authentication failed.");

            if (tokenInfo.Type == TokenType.ApiKey && tokenUse != "api_key")
                return AuthenticateResult.Fail("Bot auth scheme requires API key token.");

            if ((tokenInfo.Type == TokenType.AuthKey || tokenInfo.Type == TokenType.OidcKey) && tokenUse == "api_key")
                return AuthenticateResult.Fail("API key token must use Bot auth scheme.");

            Context.Items["CurrentUser"] = session.Account;
            Context.Items["CurrentSession"] = session;
            Context.Items["CurrentTokenType"] = tokenInfo.Type.ToString();

            var claims = new List<Claim>
            {
                new("user_id", session.Account.Id.ToString()),
                new("session_id", session.Id.ToString()),
                new("token_type", tokenInfo.Type.ToString())
            };

            session.Scopes.ForEach(scope => claims.Add(new Claim("scope", scope)));

            if (session.Account.IsSuperuser)
                claims.Add(new Claim("is_superuser", "1"));

            var identity = new ClaimsIdentity(claims, AuthConstants.SchemeName);
            var principal = new ClaimsPrincipal(identity);

            var ticket = new AuthenticationTicket(principal, AuthConstants.SchemeName);

            var lastInfo = new LastActiveInfo
            {
                Account = session.Account,
                Session = session,
                SeenAt = SystemClock.Instance.GetCurrentInstant(),
            };
            fbs.Enqueue(lastInfo);

            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
        }
    }

    private TokenInfo? _ExtractToken(HttpRequest request)
    {
        if (request.Query.TryGetValue(AuthConstants.TokenQueryParamName, out var queryToken))
        {
            return new TokenInfo
            {
                Token = queryToken.ToString(),
                Type = TokenType.AuthKey
            };
        }

        var authHeader = NormalizeAuthHeader(request.Headers.Authorization.ToString());
        if (!string.IsNullOrEmpty(authHeader))
        {
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var tokenText = authHeader["Bearer ".Length..].Trim();
                var parts = tokenText.Split('.');

                return new TokenInfo
                {
                    Token = tokenText,
                    Type = parts.Length == 3 ? TokenType.OidcKey : TokenType.AuthKey
                };
            }

            if (authHeader.StartsWith("Bot ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["Bot ".Length..].Trim(),
                    Type = TokenType.ApiKey
                };
            }

            var acceptLegacySchemes = config.GetValue("Auth:Headers:AcceptLegacySchemes", true);
            if (!acceptLegacySchemes)
                goto cookie_check;

            if (authHeader.StartsWith("AtField ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["AtField ".Length..].Trim(),
                    Type = TokenType.AuthKey
                };
            }

            if (authHeader.StartsWith("AkField ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["AkField ".Length..].Trim(),
                    Type = TokenType.ApiKey
                };
            }
        }

    cookie_check:
        if (request.Cookies.TryGetValue(AuthConstants.CookieTokenName, out var cookieToken))
        {
            return new TokenInfo
            {
                Token = cookieToken,
                Type = cookieToken.Count(c => c == '.') == 2 ? TokenType.OidcKey : TokenType.AuthKey
            };
        }

        return null;
    }

    private static string NormalizeAuthHeader(string raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(value)) return string.Empty;

        if (value.Length >= 2 && value[0] == '[' && value[^1] == ']')
            value = value[1..^1].Trim();

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1].Trim();

        return value;
    }
}
