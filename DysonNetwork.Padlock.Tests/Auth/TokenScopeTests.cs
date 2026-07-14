using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DysonNetwork.Padlock.Auth;
using Xunit;

namespace DysonNetwork.Padlock.Tests.Auth;

public class TokenScopeTests
{
    [Fact]
    public void ExtractScopesFromJwt_PreservesPermissionScopeFromAccessToken()
    {
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim("scope", "openid profile chat.messages.create"),
                new Claim("scope", "accounts.profile.board"),
            ]
        );

        var scopes = TokenAuthService.ExtractScopesFromJwt(token);

        Assert.Contains("chat.messages.create", scopes);
        Assert.Contains("accounts.profile.board", scopes);
    }
}
