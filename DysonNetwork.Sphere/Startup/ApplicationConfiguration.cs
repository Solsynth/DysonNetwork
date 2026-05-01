using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Sphere.ActivityPub;
using DysonNetwork.Sphere.ActivityPub.Services;
using DysonNetwork.Sphere.Poll;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Autocompletion;
using DysonNetwork.Sphere.Rewind;

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

        app.UseInboxRateLimiting();
        app.UseInboxValidation();
        app.UseInboxActivityParsing();

        app.MapControllers();

        // Map gRPC services
        app.MapGrpcService<PostServiceGrpc>();
        app.MapGrpcService<PollServiceGrpc>();
        app.MapGrpcService<PublisherServiceGrpc>();
        app.MapGrpcService<PublisherRatingServiceGrpc>();
        app.MapGrpcService<SphereRewindServiceGrpc>();
        app.MapGrpcService<AutocompletionServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
