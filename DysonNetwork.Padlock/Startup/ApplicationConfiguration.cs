using DysonNetwork.Padlock.Account;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Padlock.Permission;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Padlock.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(
        this WebApplication app,
        IConfiguration configuration
    )
    {
        app.MapOpenApi();

        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.MapGet(
                "/.well-known/apple-app-site-association",
                (IConfiguration config) =>
                {
                    var appId =
                        config["Authentication:Apple:AppId"] ?? "W7HPZ53V6B.dev.solsynth.solian";
                    return Results.Json(
                        new
                        {
                            applinks = new
                            {
                                details = new[]
                                {
                                    new { appIDs = new[] { appId }, components = (object?)null },
                                },
                            },
                            webcredentials = new { apps = new[] { appId } },
                            appclips = new { apps = Array.Empty<string>() },
                        }
                    );
                }
            )
            .AllowAnonymous();

        app.MapGet(
                "/.well-known/assetlinks.json",
                (IConfiguration config) =>
                {
                    var packageName =
                        config["Authentication:Android:PackageName"] ?? "dev.solsynth.solian";
                    var fingerprints =
                        config
                            .GetSection("Authentication:Android:Sha256CertFingerprints")
                            .Get<string[]>()
                        ?? [];
                    var target = new Dictionary<string, object>
                    {
                        ["namespace"] = "android_app",
                        ["package_name"] = packageName,
                        ["sha256_cert_fingerprints"] = fingerprints,
                    };
                    return Results.Json(
                        new object[]
                        {
                            new Dictionary<string, object>
                            {
                                ["relation"] = new[]
                                {
                                    "delegate_permission/common.handle_all_urls",
                                },
                                ["target"] = target,
                            },
                        }
                    );
                }
            )
            .AllowAnonymous();

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
