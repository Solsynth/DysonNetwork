using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;

namespace DysonNetwork.Messager.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<RemotePermissionMiddleware>();

        app.MapControllers();

        app.MapGrpcReflectionService();

        return app;
    }
}
