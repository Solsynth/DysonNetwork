using System.Net;
using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Permission;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;
using Prometheus;

namespace DysonNetwork.Pass.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration, string contentRoot)
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
                .WithHeaders("*")
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod()
        );

        app.UseWebSockets();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PermissionMiddleware>();

        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(contentRoot, "wwwroot", "dist"))
        });

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
        app.MapGrpcService<AccountServiceGrpc>();
        app.MapGrpcService<AuthServiceGrpc>();
        app.MapGrpcService<ActionLogServiceGrpc>();
        app.MapGrpcService<PermissionServiceGrpc>();
        app.MapGrpcService<BotAccountReceiverGrpc>();

        return app;
    }
}