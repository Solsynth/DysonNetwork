using System.Net;
using DysonNetwork.Develop.Identity;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Prometheus;

namespace DysonNetwork.Develop.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapMetrics();
        app.MapOpenApi();

        app.UseSwagger();
        app.UseSwaggerUI();
        
        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<PermissionMiddleware>();

        app.MapControllers();
        
        app.MapGrpcService<CustomAppServiceGrpc>();

        return app;
    }
}
