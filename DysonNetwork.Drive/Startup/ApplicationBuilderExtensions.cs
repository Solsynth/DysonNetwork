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

        app.UseCors(opts =>
            opts.SetIsOriginAllowed(_ => true)
                .WithExposedHeaders("*")
                .WithHeaders("*")
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod()
        );

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
