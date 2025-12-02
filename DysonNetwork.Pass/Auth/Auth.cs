using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using DysonNetwork.Pass.Handlers;
using DysonNetwork.Shared.Cache;
using SystemClock = NodaTime.SystemClock;

namespace DysonNetwork.Pass.Auth;

public static class AuthConstants
{
    public const string SchemeName = "DysonToken";
    public const string TokenQueryParamName = "tk";
    public const string CookieTokenName = "AuthToken";
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
    FlushBufferService fbs
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
            // Get client IP address
            var ipAddress = Context.Connection.RemoteIpAddress?.ToString();
            
            var (valid, session, message) = await token.AuthenticateTokenAsync(tokenInfo.Token, ipAddress);
            if (!valid || session is null)
                return AuthenticateResult.Fail(message ?? "Authentication failed.");

            // Store user and session in the HttpContext.Items for easy access in controllers
            Context.Items["CurrentUser"] = session.Account;
            Context.Items["CurrentSession"] = session;
            Context.Items["CurrentTokenType"] = tokenInfo.Type.ToString();

            // Create claims from the session
            var claims = new List<Claim>
            {
                new("user_id", session.Account.Id.ToString()),
                new("session_id", session.Id.ToString()),
                new("token_type", tokenInfo.Type.ToString())
            };

            // Add scopes as claims
            session.Scopes.ForEach(scope => claims.Add(new Claim("scope", scope)));

            // Add superuser claim if applicable
            if (session.Account.IsSuperuser)
                claims.Add(new Claim("is_superuser", "1"));

            // Create the identity and principal
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
        // Check for token in query parameters
        if (request.Query.TryGetValue(AuthConstants.TokenQueryParamName, out var queryToken))
        {
            return new TokenInfo
            {
                Token = queryToken.ToString(),
                Type = TokenType.AuthKey
            };
        }


        // Check for token in Authorization header
        var authHeader = request.Headers.Authorization.ToString();
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

        // Check for token in cookies
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
}
