using DysonNetwork.Insight.Reader;
using DysonNetwork.Shared.Http;

namespace DysonNetwork.Insight.Startup;

public static class ApplicationConfiguration
{
    public static WebApplication ConfigureAppMiddleware(this WebApplication app, IConfiguration configuration)
    {
        app.MapOpenApi();

        app.UseRequestLocalization();

        app.ConfigureForwardedHeaders(configuration);

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.MapGrpcService<WebReaderGrpcService>();
        app.MapGrpcService<WebArticleGrpcService>();
        app.MapGrpcService<WebFeedGrpcService>();
        app.MapGrpcReflectionService();

        return app;
    }
}
