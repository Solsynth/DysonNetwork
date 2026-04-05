using DysonNetwork.Padlock.Auth;
using DysonNetwork.Padlock.Account;
using DysonNetwork.Padlock.Permission;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Padlock.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapOpenApi();

        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.MapGet("/.well-known/apple-app-site-association", (IConfiguration config) =>
        {
            var appId = config["Authentication:Apple:AppId"] ?? "W7HPZ53V6B.dev.solsynth.solian";
            return Results.Json(new
            {
                applinks = new
                {
                    details = new[]
                    {
                        new
                        {
                            appIDs = new[] { appId },
                            components = (object?)null
                        }
                    }
                },
                webcredentials = new
                {
                    apps = new[] { appId }
                },
                appclips = new
                {
                    apps = Array.Empty<string>()
                }
            });
        }).AllowAnonymous();

        app.MapGet("/.well-known/assetlinks.json", (IConfiguration config) =>
        {
            var packageName = config["Authentication:Android:PackageName"] ?? "com.example.myapp";
            var fingerprints = config.GetSection("Authentication:Android:Sha256CertFingerprints").Get<string[]>() 
                ?? ["14:6D:E9:83:C5:73:06:50:D8:EE:B9:95:2F:34:FC:64:16:A0:83:42:E6:1D:BE:A8:8A:04:96:B2:3F:CF:44:E5"];
            var target = new Dictionary<string, object>
            {
                ["namespace"] = "android_app",
                ["package_name"] = packageName,
                ["sha256_cert_fingerprints"] = fingerprints
            };
            return Results.Json(new object[]
            {
                new Dictionary<string, object>
                {
                    ["relation"] = new[] { "delegate_permission/common.handle_all_urls" },
                    ["target"] = target
                }
            });
        }).AllowAnonymous();

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<AuthServiceGrpc>();
        app.MapGrpcService<AccountServiceGrpc>();
        app.MapGrpcService<BotAccountReceiverGrpc>();
        app.MapGrpcService<ActionLogServiceGrpc>();
        app.MapGrpcService<PermissionServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
