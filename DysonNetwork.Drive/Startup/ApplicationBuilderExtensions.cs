using DysonNetwork.Drive.Storage;
using tusdotnet;
using tusdotnet.Interfaces;

namespace DysonNetwork.Drive.Startup;

public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, ITusStore tusStore)
    {
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthorization();
        app.MapControllers();
        
        app.MapTus("/tus", _ => Task.FromResult(TusService.BuildConfiguration(tusStore)));

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
