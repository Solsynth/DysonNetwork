using DysonNetwork.Pass.Account;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Credit;
using DysonNetwork.Pass.Leveling;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Pass.Realm;
using DysonNetwork.Pass.Wallet;
using DysonNetwork.Shared.Http;
using Prometheus;

namespace DysonNetwork.Pass.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapMetrics();
        app.MapOpenApi();

        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PermissionMiddleware>();

        app.MapControllers().RequireRateLimiting("fixed");

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<AccountServiceGrpc>();
        app.MapGrpcService<AuthServiceGrpc>();
        app.MapGrpcService<ActionLogServiceGrpc>();
        app.MapGrpcService<PermissionServiceGrpc>();
        app.MapGrpcService<SocialCreditServiceGrpc>();
        app.MapGrpcService<ExperienceServiceGrpc>();
        app.MapGrpcService<BotAccountReceiverGrpc>();
        app.MapGrpcService<WalletServiceGrpc>();
        app.MapGrpcService<PaymentServiceGrpc>();
        app.MapGrpcService<RealmServiceGrpc>();

        return app;
    }
}
