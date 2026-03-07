using DysonNetwork.Padlock.Auth;
using DysonNetwork.Padlock.Account;
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

        app.MapControllers().RequireRateLimiting("fixed");

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<AuthServiceGrpc>();
        app.MapGrpcService<AccountServiceGrpc>();
        app.MapGrpcService<ActionLogServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
