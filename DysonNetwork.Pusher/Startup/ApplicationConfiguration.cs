using System.Net;
using DysonNetwork.Pusher.Services;
using DysonNetwork.Shared.Http;
using Microsoft.AspNetCore.HttpOverrides;

namespace DysonNetwork.Pusher.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapOpenApi();

        app.UseSwagger();
        app.UseSwaggerUI();
        
        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseCors(opts =>
            opts.SetIsOriginAllowed(_ => true)
                .WithExposedHeaders("*")
                .WithHeaders("*")
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod()
        );

        app.UseWebSockets();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers().RequireRateLimiting("fixed");

        return app;
    }
    
    public static WebApplication ConfigureGrpcServices(this WebApplication app)
    {
        app.MapGrpcService<PusherServiceGrpc>();
        
        return app;
    }
}
