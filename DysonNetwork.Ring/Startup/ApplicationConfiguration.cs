using DysonNetwork.Ring.Services;
using DysonNetwork.Shared.Http;

namespace DysonNetwork.Ring.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers().RequireRateLimiting("fixed");

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<RingServiceGrpc>();

        return app;
    }
}
