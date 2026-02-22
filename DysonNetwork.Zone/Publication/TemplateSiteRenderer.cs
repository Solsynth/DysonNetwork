using DotLiquid;
using DotLiquid.Tags;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Caching.Memory;
using System.Collections;

namespace DysonNetwork.Zone.Publication;

public class TemplateSiteRenderer
{
    private static readonly object RenderSync = new();
    private static int _liquidInitialized;

    private readonly TemplateRouteResolver _routeResolver;
    private readonly TemplateContextBuilder _contextBuilder;
    private readonly PublicationSiteManager _siteManager;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TemplateSiteRenderer> _logger;

    public TemplateSiteRenderer(
        TemplateRouteResolver routeResolver,
        TemplateContextBuilder contextBuilder,
        PublicationSiteManager siteManager,
        IMemoryCache cache,
        ILogger<TemplateSiteRenderer> logger
    )
    {
        _routeResolver = routeResolver;
        _contextBuilder = contextBuilder;
        _siteManager = siteManager;
        _cache = cache;
        _logger = logger;

        InitializeLiquid();
    }

    private static void InitializeLiquid()
    {
        if (Interlocked.Exchange(ref _liquidInitialized, 1) == 1)
            return;

        // Shopify themes commonly use {% render 'partial' %}; DotLiquid exposes include by default.
        Template.RegisterTag<Include>("render");
    }

    public async Task<TemplateRenderResult> RenderAsync(HttpContext context, SnPublicationSite site)
    {
        var route = await _routeResolver.ResolveAsync(site, context.Request.Path.Value ?? "/");
        if (route.Kind == TemplateResolutionKind.None)
            return new TemplateRenderResult { Handled = false };

        if (route.Kind == TemplateResolutionKind.StaticFile)
        {
            var fullPath = _siteManager.GetValidatedFullPath(site.Id, route.RelativePath);
            var contentType = GetContentType(route.RelativePath);
            return new TemplateRenderResult
            {
                Handled = true,
                StatusCode = route.StatusCode,
                ContentType = contentType,
                StaticFilePath = fullPath,
            };
        }

        var siteDirectory = _siteManager.GetSiteDirectory(site.Id);
        var templatePath = _siteManager.GetValidatedFullPath(site.Id, route.RelativePath);
        if (!File.Exists(templatePath))
            return new TemplateRenderResult { Handled = false };

        var contextData = await _contextBuilder.BuildAsync(site, route, context);

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
                var layoutFull = _siteManager.GetValidatedFullPath(site.Id, layoutRelative);
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
        var hash = Hash.FromDictionary(ToLiquidDictionary(contextData));

        lock (RenderSync)
        {
            Template.FileSystem = new TemplateFileSystem(siteDirectory);
            return template.Render(hash);
        }
    }

    private static Dictionary<string, object> ToLiquidDictionary(IDictionary<string, object?> source)
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in source)
        {
            result[pair.Key] = ToLiquidValue(pair.Value);
        }
        return result;
    }

    private static object ToLiquidValue(object? value)
    {
        if (value is null)
            return string.Empty;

        if (value is DateTimeOffset dto)
            return dto.UtcDateTime;

        if (value is IDictionary<string, object?> dictNullable)
            return Hash.FromDictionary(ToLiquidDictionary(dictNullable));

        if (value is IDictionary<string, object> dict)
            return Hash.FromDictionary(ToLiquidDictionary(dict.ToDictionary(x => x.Key, x => (object?)x.Value)));

        if (value is IDictionary nonGenericDict)
        {
            var converted = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (DictionaryEntry entry in nonGenericDict)
            {
                if (entry.Key is string key)
                    converted[key] = entry.Value;
            }
            return Hash.FromDictionary(ToLiquidDictionary(converted));
        }

        if (value is IEnumerable enumerable and not string)
        {
            var items = new List<object>();
            foreach (var item in enumerable)
                items.Add(ToLiquidValue(item));
            return items;
        }

        return value;
    }

    private async Task<Template> GetTemplateFromCacheAsync(Guid siteId, string relativePath, string fullPath)
    {
        var fileInfo = new FileInfo(fullPath);
        var cacheKey = $"tmpl:{siteId}:{relativePath}:{fileInfo.LastWriteTimeUtc.Ticks}:{fileInfo.Length}";

        if (_cache.TryGetValue(cacheKey, out Template? template) && template is not null)
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
            _logger.LogWarning("Liquid parse warnings in {Path}: {Message}", relativePath, message);
        }

        _cache.Set(cacheKey, parsed, TimeSpan.FromMinutes(20));
        return parsed;
    }

    private async Task<string?> ResolveLayoutRelativePathAsync(Guid siteId)
    {
        foreach (var candidate in new[] { "layout.html.liquid", "templates/layout.html.liquid" })
        {
            var fullPath = _siteManager.GetValidatedFullPath(siteId, candidate);
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
