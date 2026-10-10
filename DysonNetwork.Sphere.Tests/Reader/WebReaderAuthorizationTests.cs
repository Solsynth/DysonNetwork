using System.Net;
using System.Text.Encodings.Web;
using DysonNetwork.Sphere.Reader;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DysonNetwork.Sphere.Tests.Reader;

/// <summary>
/// The repo has no WebApplicationFactory based test host (Program.cs runs migrations,
/// Quartz jobs and gRPC/NATS wiring), so these tests exercise the real MVC pipeline on a
/// minimal in-process host and fall back to action metadata for the authorization check.
/// </summary>
public class WebReaderAuthorizationTests
{
    private const string TestScheme = "TestAuth";

    [Fact]
    public void ScrapLink_RequiresAuthorization()
    {
        var method = typeof(WebReaderController).GetMethod(nameof(WebReaderController.ScrapLink));

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).FirstOrDefault());
    }

    [Fact]
    public async Task ScrapLink_ReturnsUnauthorizedForAnonymousRequests()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(WebReaderController).Assembly);
        builder
            .Services.AddAuthentication(TestScheme)
            .AddScheme<AuthenticationSchemeOptions, AnonymousOnlyHandler>(TestScheme, _ => { });
        builder.Services.AddAuthorization();

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync();
        try
        {
            var address = app
                .Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.First();

            using var client = new HttpClient();
            var anonymous = await client.GetAsync(
                $"{address}/api/scrap/link?url=https%3A%2F%2Fexample.com%2F"
            );
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            // The caching siblings were already protected, the read endpoint now matches them.
            var invalidate = await client.DeleteAsync($"{address}/api/scrap/link/cache?url=https%3A%2F%2Fexample.com%2F");
            Assert.Equal(HttpStatusCode.Unauthorized, invalidate.StatusCode);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private sealed class AnonymousOnlyHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }
}
