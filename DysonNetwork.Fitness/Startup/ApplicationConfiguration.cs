using DysonNetwork.Fitness;

namespace DysonNetwork.Fitness.Startup;

public static class ApplicationConfiguration
{
    public static void ConfigureAppMiddleware(this IApplicationBuilder app, IConfiguration configuration)
    {
        app.UseRouting();
        
        app.UseAuthorization();
        
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapGrpcService<DysonNetwork.Fitness.FitnessGrpcService>();
            endpoints.MapGrpcReflectionService();
        });
    }
}
