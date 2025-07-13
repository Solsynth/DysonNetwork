using System.Security.Claims;
using System.Text.Encodings.Web;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SystemClock = NodaTime.SystemClock;

namespace DysonNetwork.Shared.Auth;

public class DysonTokenAuthOptions : AuthenticationSchemeOptions;

public class DysonTokenAuthHandler(
    IOptionsMonitor<DysonTokenAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISystemClock clock,
    AuthService.AuthServiceClient auth
)
    : AuthenticationHandler<DysonTokenAuthOptions>(options, logger, encoder, clock)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tokenInfo = _ExtractToken(Request);

        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.Token))
            return AuthenticateResult.Fail("No token was provided.");

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();

            // Validate token and extract session ID
            AuthSession session;
            try
            {
                session = await ValidateToken(tokenInfo.Token);
            }
            catch (InvalidOperationException ex)
            {
                return AuthenticateResult.Fail(ex.Message);
            }
            catch (RpcException ex)
            {
                return AuthenticateResult.Fail($"Remote error: {ex.Status.StatusCode} - {ex.Status.Detail}");
            }

            // Store user and session in the HttpContext.Items for easy access in controllers
            Context.Items["CurrentUser"] = session.Account;
            Context.Items["CurrentSession"] = session;
            Context.Items["CurrentTokenType"] = tokenInfo.Type.ToString();

            // Create claims from the session
            var claims = new List<Claim>
            {
                new("user_id", session.Account.Id),
                new("session_id", session.Id),
                new("token_type", tokenInfo.Type.ToString())
            };

            // return AuthenticateResult.Success(ticket);
            return AuthenticateResult.NoResult();
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
        }
    }

    private async Task<AuthSession> ValidateToken(string token)
    {
        var resp = await auth.AuthenticateAsync(new AuthenticateRequest { Token = token });
        if (!resp.Valid) throw new InvalidOperationException(resp.Message);
        if (resp.Session == null) throw new InvalidOperationException("Session not found.");
        return resp.Session;
    }

    private static byte[] Base64UrlDecode(string base64Url)
    {
        var padded = base64Url
            .Replace('-', '+')
            .Replace('_', '/');

        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    private static TokenInfo? _ExtractToken(HttpRequest request)
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
        var authHeader = request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(authHeader))
        {
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authHeader["Bearer ".Length..].Trim();
                var parts = token.Split('.');

                return new TokenInfo
                {
                    Token = token,
                    Type = parts.Length == 3 ? TokenType.OidcKey : TokenType.AuthKey
                };
            }
            else if (authHeader.StartsWith("AtField ", StringComparison.OrdinalIgnoreCase))
            {
                return new TokenInfo
                {
                    Token = authHeader["AtField ".Length..].Trim(),
                    Type = TokenType.AuthKey
                };
            }
            else if (authHeader.StartsWith("AkField ", StringComparison.OrdinalIgnoreCase))
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