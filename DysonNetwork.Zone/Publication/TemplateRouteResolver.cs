using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Zone.Publication;

public class TemplateRouteResolver(PublicationSiteManager siteManager)
{
    private readonly JsonSerializerOptions _jsonOptions =
        InfraObjectCoder.SerializerOptions ?? new JsonSerializerOptions();

    public async Task<TemplateRouteResolution> ResolveAsync(SnPublicationSite site, string currentPath)
    {
        var normalizedPath = NormalizePath(currentPath);

        // Convention lookup first
        var conventional = ResolveByConvention(site, normalizedPath);

        // Optional manifest can override/extend
        var manifest = await LoadManifestAsync(site.Id);
        if (manifest is not null)
        {
            var manifestMatch = ResolveByManifest(site, normalizedPath, manifest);
            if (manifestMatch is not null)
                return manifestMatch;
        }

        if (conventional is not null)
            return conventional;

        // 404 fallback template
        foreach (var notFound in new[] { "404.html.liquid", "templates/404.html.liquid" })
        {
            if (!TryResolveExistingPath(site.Id, notFound, out var kind))
                continue;

            return new TemplateRouteResolution
            {
                Kind = kind,
                RelativePath = notFound,
                StatusCode = StatusCodes.Status404NotFound,
                PageType = "page"
            };
        }

        return new TemplateRouteResolution { Kind = TemplateResolutionKind.None };
    }

    private TemplateRouteResolution? ResolveByConvention(SnPublicationSite site, string normalizedPath)
    {
        var pathWithoutSlash = normalizedPath.TrimStart('/');

        var directCandidates = new List<string>();
        if (!string.IsNullOrEmpty(pathWithoutSlash))
            directCandidates.Add(pathWithoutSlash);

        if (string.IsNullOrEmpty(pathWithoutSlash))
            directCandidates.Add("index.html.liquid");
        else if (!Path.HasExtension(pathWithoutSlash))
        {
            directCandidates.Add($"{pathWithoutSlash}.html.liquid");
            directCandidates.Add($"{pathWithoutSlash}/index.html.liquid");
            directCandidates.Add($"templates/{pathWithoutSlash}.html.liquid");
            directCandidates.Add($"templates/{pathWithoutSlash}/index.html.liquid");
        }

        foreach (var candidate in directCandidates)
        {
            if (!TryResolveExistingPath(site.Id, candidate, out var kind))
                continue;

            return new TemplateRouteResolution
            {
                Kind = kind,
                RelativePath = candidate,
                PageType = InferPageType(normalizedPath, candidate)
            };
        }

        if (!string.IsNullOrEmpty(pathWithoutSlash) && !Path.HasExtension(pathWithoutSlash))
        {
            foreach (var candidate in new[] { $"{pathWithoutSlash}/index.html", $"templates/{pathWithoutSlash}/index.html" })
            {
                if (!TryResolveExistingPath(site.Id, candidate, out var kind) || kind != TemplateResolutionKind.StaticFile)
                    continue;

                return new TemplateRouteResolution
                {
                    Kind = kind,
                    RelativePath = candidate,
                    PageType = InferPageType(normalizedPath, candidate)
                };
            }
        }

        return null;
    }

    private TemplateRouteResolution? ResolveByManifest(
        SnPublicationSite site,
        string normalizedPath,
        TemplateRouteManifest manifest
    )
    {
        foreach (var route in manifest.Routes)
        {
            if (!TryMatchRoute(route.Path, normalizedPath, out var routeParams))
                continue;

            if (!string.IsNullOrWhiteSpace(route.RedirectTo))
            {
                var redirectStatus = route.RedirectStatus is 301 or 302 or 307 or 308
                    ? route.RedirectStatus.Value
                    : StatusCodes.Status302Found;

                return new TemplateRouteResolution
                {
                    Kind = TemplateResolutionKind.Redirect,
                    RedirectTo = route.RedirectTo,
                    StatusCode = redirectStatus,
                    PageType = "page",
                    RouteEntry = route,
                    RouteParams = routeParams,
                };
            }

            var templatePath = route.Template.Replace('\\', '/').TrimStart('/');
            if (!TryResolveExistingPath(site.Id, templatePath, out var kind))
                continue;

            var pageType = string.IsNullOrWhiteSpace(route.PageType)
                ? InferPageType(normalizedPath, templatePath)
                : route.PageType!.Trim();

            return new TemplateRouteResolution
            {
                Kind = kind,
                RelativePath = templatePath,
                PageType = pageType,
                RouteEntry = route,
                RouteParams = routeParams,
            };
        }

        return null;
    }

    private bool TryResolveExistingPath(Guid siteId, string relativePath, out TemplateResolutionKind kind)
    {
        kind = TemplateResolutionKind.None;

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
            return false;

        string fullPath;
        try
        {
            fullPath = siteManager.GetValidatedFullPath(siteId, normalized);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (File.Exists(fullPath))
        {
            kind = normalized.EndsWith(".liquid", StringComparison.OrdinalIgnoreCase)
                ? TemplateResolutionKind.Template
                : TemplateResolutionKind.StaticFile;
            return true;
        }

        return false;
    }

    private async Task<TemplateRouteManifest?> LoadManifestAsync(Guid siteId)
    {
        var candidates = new[] { "routes.json", "templates/routes.json" };

        foreach (var candidate in candidates)
        {
            string fullPath;
            try
            {
                fullPath = siteManager.GetValidatedFullPath(siteId, candidate);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (!File.Exists(fullPath))
                continue;

            await using var stream = File.OpenRead(fullPath);
            var manifest = await JsonSerializer.DeserializeAsync<TemplateRouteManifest>(stream, _jsonOptions);
            if (manifest is not null)
                return manifest;
        }

        return null;
    }

    private static string NormalizePath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
            return "/";

        var path = currentPath.Replace('\\', '/');
        if (!path.StartsWith('/'))
            path = "/" + path;

        return path;
    }

    private static bool TryMatchRoute(string pattern, string path, out Dictionary<string, string> routeParams)
    {
        routeParams = [];

        var normalizedPattern = NormalizePath(pattern).Trim('/');
        var normalizedPath = NormalizePath(path).Trim('/');

        var pSegments = string.IsNullOrEmpty(normalizedPattern)
            ? []
            : normalizedPattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var vSegments = string.IsNullOrEmpty(normalizedPath)
            ? []
            : normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (pSegments.Length != vSegments.Length)
            return false;

        for (var i = 0; i < pSegments.Length; i++)
        {
            var p = pSegments[i];
            var v = vSegments[i];

            if (p.StartsWith('{') && p.EndsWith('}') && p.Length > 2)
            {
                var key = p[1..^1];
                routeParams[key] = Uri.UnescapeDataString(v);
                continue;
            }

            if (!string.Equals(p, v, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string InferPageType(string normalizedPath, string templatePath)
    {
        if (string.Equals(normalizedPath, "/", StringComparison.Ordinal))
            return "home";

        var name = Path.GetFileName(templatePath);
        if (name.Contains("post", StringComparison.OrdinalIgnoreCase) || normalizedPath.Contains("/posts/", StringComparison.OrdinalIgnoreCase))
            return "post";

        if (name.Contains("archive", StringComparison.OrdinalIgnoreCase))
            return "archive";

        if (name.Contains("category", StringComparison.OrdinalIgnoreCase))
            return "category";

        if (name.Contains("tag", StringComparison.OrdinalIgnoreCase))
            return "tag";

        return "page";
    }
}
