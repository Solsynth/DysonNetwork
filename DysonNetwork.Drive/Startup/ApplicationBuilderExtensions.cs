using DysonNetwork.Drive.Storage;
using Microsoft.Extensions.FileProviders;
using tusdotnet;
using tusdotnet.Interfaces;

namespace DysonNetwork.Drive.Startup;

public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, ITusStore tusStore, string contentRoot)
    {
        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthorization();
        app.MapControllers();
        
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(contentRoot, "wwwroot", "dist"))
        });
        
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
