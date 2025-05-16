using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DysonNetwork.Sphere.Auth;

public static class AuthConstants
{
    public const string SchemeName = "DysonToken";
    public const string TokenQueryParamName = "tk";
}

public class DysonTokenAuthOptions : AuthenticationSchemeOptions;

public class DysonTokenAuthHandler : AuthenticationHandler<DysonTokenAuthOptions>
{
    private TokenValidationParameters _tokenValidationParameters;
    
    public DysonTokenAuthHandler(
        IOptionsMonitor<DysonTokenAuthOptions> options,
        IConfiguration configuration,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
        var publicKey = File.ReadAllText(configuration["Jwt:PublicKeyPath"]!);
        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        _tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "solar-network",
            IssuerSigningKey = new RsaSecurityKey(rsa)
        };
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = _ExtractToken(Request);

        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.Fail("No token was provided.");

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, _tokenValidationParameters, out var validatedToken);

            var ticket = new AuthenticationTicket(principal, AuthConstants.SchemeName);
            return AuthenticateResult.Success(ticket);
        }
        catch (Exception ex)
        {
            return AuthenticateResult.Fail($"Authentication failed: {ex.Message}");
        }
    }

    private static string? _ExtractToken(HttpRequest request)
    {
        if (request.Query.TryGetValue(AuthConstants.TokenQueryParamName, out var queryToken))
            return queryToken;

        var authHeader = request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim();

        return request.Cookies.TryGetValue(AuthConstants.TokenQueryParamName, out var cookieToken)
            ? cookieToken
            : null;
    }
}