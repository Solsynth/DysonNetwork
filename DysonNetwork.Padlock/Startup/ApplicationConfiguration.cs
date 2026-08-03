using DysonNetwork.Padlock.Account;
using DysonNetwork.Padlock.Auth;
using DysonNetwork.Padlock.E2EE;
using DysonNetwork.Padlock.Permission;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Padlock.Startup;

public static class ApplicationConfiguration
{
    // Routes of the Solian (Island) client, mapped 1:1 for Universal Links.
    // Apple matches components top-down and uses the first hit, so literal
    // paths precede wildcard patterns that would otherwise shadow them.
    // Keep in sync with the client route table (Island: lib/route.dart).
    private static readonly Dictionary<string, object>[] AppLinkComponents =
    [
        AasaComponent("/"),
        AasaComponent("/explore"),
        AasaComponent("/chat"),
        AasaComponent("/chat/search"),
        AasaComponent("/chat/*/detail"),
        AasaComponent("/chat/*/search"),
        AasaComponent("/chat/*"),
        AasaComponent("/realms"),
        AasaComponent("/account"),
        AasaComponent("/account/stickers"),
        AasaComponent("/account/stickers/*"),
        AasaComponent("/account/relationships"),
        AasaComponent("/account/me/update"),
        AasaComponent("/account/me/activation"),
        AasaComponent("/account/me/board"),
        AasaComponent("/account/me/leveling"),
        AasaComponent("/account/me/settings"),
        AasaComponent("/account/me/qr"),
        AasaComponent("/account/me/badges"),
        AasaComponent("/account/me/progress"),
        AasaComponent("/account/me/meet"),
        AasaComponent("/account/me/meet/*"),
        AasaComponent("/account/me/action-logs"),
        AasaComponent("/account/me/physical-passports"),
        AasaComponent("/account/tickets"),
        AasaComponent("/account/tickets/*"),
        AasaComponent("/account/me/punishments"),
        AasaComponent("/account/me/affiliations"),
        AasaComponent("/account/me/affiliations/*"),
        AasaComponent("/files"),
        AasaComponent("/files/*"),
        AasaComponent("/creators"),
        AasaComponent("/creators/*/posts"),
        AasaComponent("/creators/*/collections"),
        AasaComponent("/creators/*/surveys"),
        AasaComponent("/creators/*/stickers"),
        AasaComponent("/creators/*/stickers/*"),
        AasaComponent("/creators/*/domains"),
        AasaComponent("/creators/*/tags"),
        AasaComponent("/wallet"),
        AasaComponent("/wallet/transactions/*"),
        AasaComponent("/articles/compose"),
        AasaComponent("/articles/*/edit"),
        AasaComponent("/blogs/compose"),
        AasaComponent("/blogs/*/edit"),
        AasaComponent("/auth/login"),
        AasaComponent("/auth/create-account"),
        AasaComponent("/auth/authorize"),
        AasaComponent("/settings"),
        AasaComponent("/settings/chat-room-storage"),
        AasaComponent("/plugins"),
        AasaComponent("/plugins/editor"),
        AasaComponent("/about"),
        AasaComponent("/cf-ip-speed-test"),
        AasaComponent("/posts/shuffle"),
        AasaComponent("/posts/bookmarks"),
        AasaComponent("/posts/categories"),
        AasaComponent("/posts/categories/*"),
        AasaComponent("/posts/*"),
        AasaComponent("/publishers/*"),
        AasaComponent("/fediverse/actors/*"),
        AasaComponent("/accounts/*"),
        AasaComponent("/search"),
        AasaComponent("/calendar/*/events/*"),
        AasaComponent("/calendar/*"),
        AasaComponent("/realms/*"),
        AasaComponent("/surveys/*"),
        AasaComponent("/orders/*"),
    ];

    private static Dictionary<string, object> AasaComponent(string pattern) =>
        new() { ["/"] = pattern };

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
        app.UseMiddleware<LocalPermissionMiddleware>();
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
                                    new
                                    {
                                        appIDs = new[] { appId },
                                        components = AppLinkComponents,
                                    },
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
        app.MapGrpcService<MlsServiceGrpc>();
        app.MapGrpcService<BoardAuthServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
