using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;

namespace DysonNetwork.Sphere.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseWebSockets();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<RemotePermissionMiddleware>();

        app.MapControllers();

        // Map gRPC services
        app.MapGrpcService<PostServiceGrpc>();
        app.MapGrpcService<PublisherServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
