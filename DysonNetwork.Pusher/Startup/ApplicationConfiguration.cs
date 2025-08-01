using System.Net;
using DysonNetwork.Pusher.Services;
using Microsoft.AspNetCore.HttpOverrides;

namespace DysonNetwork.Pusher.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapOpenApi();

        app.UseSwagger();
        app.UseSwaggerUI();
        
        app.UseRequestLocalization();

        ConfigureForwardedHeaders(app, configuration);

        app.UseCors(opts =>
            opts.SetIsOriginAllowed(_ => true)
                .WithExposedHeaders("*")
                .WithHeaders("*")
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod()
        );

        app.UseWebSockets();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers().RequireRateLimiting("fixed");

        return app;
    }

    private static void ConfigureForwardedHeaders(WebApplication app, IConfiguration configuration)
    {
        var knownProxiesSection = configuration.GetSection("KnownProxies");
        var forwardedHeadersOptions = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.All };

        if (knownProxiesSection.Exists())
        {
            var proxyAddresses = knownProxiesSection.Get<string[]>();
            if (proxyAddresses != null)
                foreach (var proxy in proxyAddresses)
                    if (IPAddress.TryParse(proxy, out var ipAddress))
                        forwardedHeadersOptions.KnownProxies.Add(ipAddress);
        }
        else
        {
            forwardedHeadersOptions.KnownProxies.Add(IPAddress.Any);
            forwardedHeadersOptions.KnownProxies.Add(IPAddress.IPv6Any);
        }

        app.UseForwardedHeaders(forwardedHeadersOptions);
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<PusherServiceGrpc>();
        
        return app;
    }
}
