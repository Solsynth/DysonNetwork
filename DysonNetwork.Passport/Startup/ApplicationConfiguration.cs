using DysonNetwork.Passport.Account;
using DysonNetwork.Passport.Credit;
using DysonNetwork.Passport.Leveling;
using DysonNetwork.Passport.Permission;
using DysonNetwork.Passport.Realm;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Passport.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapOpenApi();

        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseDyAuthModelProjection();
        app.UseAuthorization();
        app.UseMiddleware<LocalPermissionMiddleware>();

        app.MapControllers().RequireRateLimiting("fixed");

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<AccountServiceGrpc>();
        app.MapGrpcService<ActionLogServiceGrpc>();
        app.MapGrpcService<PermissionServiceGrpc>();
        app.MapGrpcService<SocialCreditServiceGrpc>();
        app.MapGrpcService<ExperienceServiceGrpc>();
        app.MapGrpcService<BotAccountReceiverGrpc>();
        app.MapGrpcService<RealmServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
