using System.Net;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Sphere.Connection;
using Microsoft.AspNetCore.HttpOverrides;
using Prometheus;

namespace DysonNetwork.Sphere.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapMetrics();
        app.MapOpenApi();

        app.UseSwagger();
        app.UseSwaggerUI();
        
        app.UseRequestLocalization();

        ConfigureForwardedHeaders(app, configuration);

        app.UseWebSockets();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PermissionMiddleware>();

        app.MapControllers().RequireRateLimiting("fixed");
        app.MapStaticAssets().RequireRateLimiting("fixed");
        app.MapRazorPages().RequireRateLimiting("fixed");

        // Map gRPC services
        app.MapGrpcService<WebSocketHandlerGrpc>();

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
}
