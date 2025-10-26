using DysonNetwork.Drive.Storage;

namespace DysonNetwork.Drive.Startup;

public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app)
    {
        app.UseAuthorization();
        app.MapControllers();

        return app;
    }

    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        // Map your gRPC services here
        app.MapGrpcService<FileServiceGrpc>();
        app.MapGrpcService<FileReferenceServiceGrpc>();
        app.MapGrpcReflectionService();

        return app;
    }
}
