using System.Net;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.HttpOverrides;
using Prometheus;
using tusdotnet;
using tusdotnet.Stores;

namespace DysonNetwork.Sphere.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration, TusDiskStore tusDiskStore)
    {
        app.MapMetrics();
        app.MapOpenApi();

        app.UseSwagger();
        app.UseSwaggerUI();
        
        app.UseRequestLocalization();

        ConfigureForwardedHeaders(app, configuration);

        app.UseCors(opts =>
            opts.SetIsOriginAllowed(_ => true)
                .WithExposedHeaders("*")
                .WithHeaders()
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod()
        );

        app.UseWebSockets();
        app.UseRateLimiter();
        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.UseMiddleware<PermissionMiddleware>();

        app.MapControllers().RequireRateLimiting("fixed");
        app.MapStaticAssets().RequireRateLimiting("fixed");
        app.MapRazorPages().RequireRateLimiting("fixed");

        app.MapTus("/files/tus", _ => Task.FromResult(TusService.BuildConfiguration(tusDiskStore)));

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
