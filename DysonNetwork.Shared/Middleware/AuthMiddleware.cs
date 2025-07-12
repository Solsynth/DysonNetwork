using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using DysonNetwork.Shared.Proto;
using System.Threading.Tasks;
using DysonNetwork.Shared.Auth;

namespace DysonNetwork.Shared.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, AuthService.AuthServiceClient authServiceClient)
    {
        var tokenInfo = _ExtractToken(context.Request);

        if (tokenInfo == null || string.IsNullOrEmpty(tokenInfo.Token))
        {
            await _next(context);
            return;
        }

        try
        {
            var authSession = await authServiceClient.AuthenticateAsync(new AuthenticateRequest { Token = tokenInfo.Token });
            context.Items["AuthSession"] = authSession;
            context.Items["CurrentTokenType"] = tokenInfo.Type.ToString();
            // Assuming AuthSession contains Account information or can be retrieved
            // context.Items["CurrentUser"] = authSession.Account; // You might need to fetch Account separately if not embedded
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "Authentication failed for token: {Token}", tokenInfo.Token);
            // Optionally, you can return an unauthorized response here
            // context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            // return;
        }

        await _next(context);
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
