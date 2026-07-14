using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DysonNetwork.Padlock.Tests.Auth;

public class AuthTokenExtractionTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    [Fact]
    public void ExtractToken_PrefersAuthorizationHeaderOverQueryAndCookie()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer header-token";
        context.Request.QueryString = new QueryString("?tk=query-token");
        context.Request.Headers.Cookie = "AuthToken=cookie-token";

        var result = DysonTokenAuthHandler.ExtractToken(context.Request, Configuration);

        Assert.NotNull(result);
        Assert.Equal("header-token", result.Token);
        Assert.Equal(TokenType.AuthKey, result.Type);
    }

    [Fact]
    public void ExtractToken_PrefersQueryTokenOverCookieForWebSocketCompatibility()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tk=query-token");
        context.Request.Headers.Cookie = "AuthToken=cookie-token";

        var result = DysonTokenAuthHandler.ExtractToken(context.Request, Configuration);

        Assert.NotNull(result);
        Assert.Equal("query-token", result.Token);
    }
}
