using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DysonNetwork.Padlock.Auth.OidcProvider.Options;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.Extensions.Options;
using NodaTime;

namespace DysonNetwork.Padlock.Auth.OidcProvider.Services;

public class OidcProviderService(
    AppDatabase db,
    AuthService auth,
    ICacheService cache,
    IOptions<OidcProviderOptions> options,
    ILogger<OidcProviderService> logger
)
{
    private readonly OidcProviderOptions _options = options.Value;

    private const string CacheKeyPrefixClientId = "auth:oidc-client:id:";
    private const string CacheKeyPrefixClientSlug = "auth:oidc-client:slug:";
    private const string CacheKeyPrefixAuthCode = "auth:oidc-code:";
    private const string CodeChallengeMethodS256 = "S256";
    private const string CodeChallengeMethodPlain = "PLAIN";

    public (bool IsValid, JwtSecurityToken? Token) ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(token);
            return (true, jwtToken);
        }
        catch
        {
            return (false, null);
        }
    }
}
