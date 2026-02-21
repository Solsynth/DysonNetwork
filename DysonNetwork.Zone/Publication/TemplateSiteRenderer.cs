using DotLiquid;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Zone.Publication;

public class TemplateSiteRenderer(
    TemplateRouteResolver routeResolver,
    TemplateContextBuilder contextBuilder,
    PublicationSiteManager siteManager,
    IMemoryCache cache,
    ILogger<TemplateSiteRenderer> logger
)
{
    private static readonly object RenderSync = new();

    public async Task<TemplateRenderResult> RenderAsync(HttpContext context, SnPublicationSite site)
    {
        var route = await routeResolver.ResolveAsync(site, context.Request.Path.Value ?? "/");
        if (route.Kind == TemplateResolutionKind.None)
            return new TemplateRenderResult { Handled = false };

        if (route.Kind == TemplateResolutionKind.StaticFile)
        {
            var fullPath = siteManager.GetValidatedFullPath(site.Id, route.RelativePath);
            var contentType = GetContentType(route.RelativePath);
            return new TemplateRenderResult
            {
                Handled = true,
                StatusCode = route.StatusCode,
                ContentType = contentType,
                StaticFilePath = fullPath,
            };
        }

        var siteDirectory = siteManager.GetSiteDirectory(site.Id);
        var templatePath = siteManager.GetValidatedFullPath(site.Id, route.RelativePath);
        if (!File.Exists(templatePath))
            return new TemplateRenderResult { Handled = false };

        var contextData = await contextBuilder.BuildAsync(site, route, context);

        var output = await RenderTemplateAsync(
            site.Id,
            siteDirectory,
            route.RelativePath,
            templatePath,
            contextData
        );

        if (!route.RelativePath.EndsWith("layout.html.liquid", StringComparison.OrdinalIgnoreCase))
        {
            var layoutRelative = await ResolveLayoutRelativePathAsync(site.Id);
            if (layoutRelative is not null)
            {
                var layoutFull = siteManager.GetValidatedFullPath(site.Id, layoutRelative);
                if (File.Exists(layoutFull))
                {
                    contextData["content_for_layout"] = output;
                    output = await RenderTemplateAsync(
                        site.Id,
                        siteDirectory,
                        layoutRelative,
                        layoutFull,
                        contextData
                    );
                }
            }
        }

        return new TemplateRenderResult
        {
            Handled = true,
            StatusCode = route.StatusCode,
            ContentType = "text/html; charset=utf-8",
            Content = output,
        };
    }

    private async Task<string> RenderTemplateAsync(
        Guid siteId,
        string siteDirectory,
        string relativePath,
        string fullPath,
        Dictionary<string, object?> contextData
    )
    {
        var template = await GetTemplateFromCacheAsync(siteId, relativePath, fullPath);
        var hash = Hash.FromDictionary(contextData);

        lock (RenderSync)
        {
            Template.FileSystem = new TemplateFileSystem(siteDirectory);
            return template.Render(hash);
        }
    }

    private async Task<Template> GetTemplateFromCacheAsync(Guid siteId, string relativePath, string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        var cacheKey = $"tmpl:{siteId}:{relativePath}:{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}";

        if (cache.TryGetValue(cacheKey, out Template? template) && template is not null)
            return template;

        var content = await File.ReadAllTextAsync(fullPath);

        Template parsed;
        lock (RenderSync)
        {
            parsed = Template.Parse(content);
        }

        if (parsed.Errors.Count > 0)
        {
            var message = string.Join(" | ", parsed.Errors.Select(e => e.ToString()));
            logger.LogWarning("Liquid parse warnings in {Path}: {Message}", relativePath, message);
        }

        cache.Set(cacheKey, parsed, TimeSpan.FromMinutes(20));
        return parsed;
    }

    private async Task<string?> ResolveLayoutRelativePathAsync(Guid siteId)
    {
        foreach (var candidate in new[] { "layout.html.liquid", "templates/layout.html.liquid" })
        {
            var fullPath = siteManager.GetValidatedFullPath(siteId, candidate);
            if (File.Exists(fullPath))
                return candidate;
        }

        await Task.CompletedTask;
        return null;
    }

    private static string GetContentType(string relativePath)
    {
        var provider = new FileExtensionContentTypeProvider();
        return provider.TryGetContentType(relativePath, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
