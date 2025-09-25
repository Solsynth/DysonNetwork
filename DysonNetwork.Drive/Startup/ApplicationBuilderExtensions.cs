using DysonNetwork.Drive.Storage;
using tusdotnet;
using tusdotnet.Interfaces;

namespace DysonNetwork.Drive.Startup;

public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, ITusStore tusStore)
    {
        app.UseAuthorization();
        app.MapControllers();

        app.MapTus("/api/tus", _ => Task.FromResult(TusService.BuildConfiguration(tusStore, app.Configuration)));

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        // Map your gRPC services here
        app.MapGrpcService<FileServiceGrpc>();
        app.MapGrpcService<FileReferenceServiceGrpc>();

        return app;
    }
}
