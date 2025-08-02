using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using OpenGraphNet;

namespace DysonNetwork.Shared.PageData;

public static class PageStartup
{
    /// <summary>
    /// The method setup the single page application routes for you.
    /// Before you calling this, ensure you have setup the static files and default files:
    /// <code>
    ///    app.UseDefaultFiles();
    ///    app.UseStaticFiles(new StaticFileOptions
    ///    {
    ///        FileProvider = new PhysicalFileProvider(Path.Combine(contentRoot, "wwwroot", "dist"))
    ///    });
    /// </code>
    /// </summary>
    /// <param name="app"></param>
    /// <param name="defaultFile"></param>
    /// <returns></returns>
    public static WebApplication MapPages(this WebApplication app, string defaultFile)
    {
        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
        }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        
#pragma warning disable ASP0016
        app.MapFallback(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/cgi"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Not found");
                return;
            }

            var html = await File.ReadAllTextAsync(defaultFile);

            using var scope = app.Services.CreateScope();
            var providers = scope.ServiceProvider.GetServices<IPageDataProvider>();

            var matches = providers
                .Where(p => p.CanHandlePath(context.Request.Path))
                .Select(p => p.GetAppDataAsync(context))
                .ToList();
            var results = await Task.WhenAll(matches);

            var appData = new Dictionary<string, object?>();
            foreach (var result in results)
            foreach (var (key, value) in result)
                appData[key] = value;

            OpenGraph? og = null;
            if (appData.TryGetValue("OpenGraph", out var openGraph) && openGraph is OpenGraph gog)
                og = gog;

            var json = JsonSerializer.Serialize(appData, jsonOpts);
            html = html.Replace("<app-data />", $"<script>window.DyPrefetch = {json};</script>");
            
            if (og is not null)
                html = html.Replace("<og-data />", og.ToString());

            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync(html);
        });
#pragma warning restore ASP0016

        return app;
    }
}