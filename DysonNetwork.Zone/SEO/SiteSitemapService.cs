using System.Text.Json;
using System.Xml;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Zone.Publication;

namespace DysonNetwork.Zone.SEO;

public class SiteSitemapService(
    DyPostService.DyPostServiceClient postClient,
    PublicationSiteManager siteManager,
    TemplateRouteResolver routeResolver
)
{
    private readonly JsonSerializerOptions _jsonOptions =
        InfraObjectCoder.SerializerOptions ?? new JsonSerializerOptions();

    public async Task<string?> RenderSitemapIfMatched(HttpContext context, SnPublicationSite site)
    {
        var sitemap = site.Config.Sitemap;
        if (sitemap is not { Enabled: true })
            return null;

        var requestPath = NormalizePath(context.Request.Path.Value ?? "/");
        var sitemapPath = NormalizePath(string.IsNullOrWhiteSpace(sitemap.Path) ? "/sitemap.xml" : sitemap.Path!);
        if (!string.Equals(requestPath, sitemapPath, StringComparison.OrdinalIgnoreCase))
            return null;

        var routeData = await ResolveRouteDataAsync(site, sitemap);
        var options = BuildEffectiveOptions(site, sitemap, routeData);

        var urls = new Dictionary<string, DateTimeOffset?>(StringComparer.OrdinalIgnoreCase);
        var baseUrl = ResolveBaseUrl(site, context);

        if (options.IncludeHome)
            urls[$"{baseUrl}/"] = null;

        if (options.IncludeRoutePaths)
        {
            var routePaths = await LoadManifestRoutePathsAsync(site.Id);
            foreach (var routePath in routePaths)
                urls[$"{baseUrl}{routePath}"] = null;
        }

        var allPosts = new List<SnPost>();
        foreach (var publisherId in options.PublisherIds)
        {
            var posts = await FetchPostsForPublisherAsync(publisherId, options);
            allPosts.AddRange(posts);
        }

        var filtered = allPosts
            .Where(p => options.IncludeReplies || p.RepliedPostId == null)
            .Where(p => options.IncludeForwards || p.ForwardedPostId == null)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderByDescending(p => GetOrderKey(p, options.OrderBy))
            .ToList();

        if (!options.OrderDesc)
            filtered.Reverse();

        foreach (var post in filtered.Take(options.ItemLimit))
        {
            var postPath = BuildPostPath(post, sitemap);
            var loc = $"{baseUrl}{postPath}";
            var lastmod = post.EditedAt?.ToDateTimeOffset()
                          ?? post.PublishedAt?.ToDateTimeOffset()
                          ?? post.UpdatedAt.ToDateTimeOffset();
            urls[loc] = lastmod;
        }

        await using var sw = new StringWriter();
        await using var writer = XmlWriter.Create(sw, new XmlWriterSettings
        {
            Async = true,
            Indent = true,
        });

        writer.WriteStartDocument();
        writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

        foreach (var item in urls)
        {
            writer.WriteStartElement("url");
            writer.WriteElementString("loc", item.Key);
            if (item.Value.HasValue)
                writer.WriteElementString("lastmod", item.Value.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
        await writer.FlushAsync();

        return sw.ToString();
    }

    private async Task<TemplateRouteDataOptions?> ResolveRouteDataAsync(
        SnPublicationSite site,
        PublicationSiteSitemapConfig sitemap
    )
    {
        if (string.IsNullOrWhiteSpace(sitemap.SourceRoutePath))
            return null;

        try
        {
            var route = await routeResolver.ResolveAsync(site, sitemap.SourceRoutePath!);
            return route.RouteEntry?.Data;
        }
        catch
        {
            return null;
        }
    }

    private static EffectiveSitemapOptions BuildEffectiveOptions(
        SnPublicationSite site,
        PublicationSiteSitemapConfig sitemap,
        TemplateRouteDataOptions? routeData
    )
    {
        var itemLimit = Math.Clamp(sitemap.ItemLimit <= 0 ? 1000 : sitemap.ItemLimit, 1, 50000);

        var publisherIds = sitemap.PublisherIds;
        if (publisherIds.Count == 0 && routeData?.PublisherIds is { Count: > 0 })
            publisherIds = routeData.PublisherIds;
        if (publisherIds.Count == 0)
            publisherIds = [site.PublisherId.ToString()];

        var types = sitemap.Types;
        if (types.Count == 0 && routeData?.Types is { Count: > 0 })
            types = routeData.Types;
        if (types.Count == 0)
            types = ["article"];

        var categories = sitemap.Categories;
        if (categories.Count == 0 && routeData?.Categories is { Count: > 0 })
            categories = routeData.Categories;

        var tags = sitemap.Tags;
        if (tags.Count == 0 && routeData?.Tags is { Count: > 0 })
            tags = routeData.Tags;

        var query = string.IsNullOrWhiteSpace(sitemap.Query)
            ? routeData?.Query
            : sitemap.Query;

        var orderBy = string.IsNullOrWhiteSpace(sitemap.OrderBy)
            ? (string.IsNullOrWhiteSpace(routeData?.OrderBy) ? "published_at" : routeData!.OrderBy!)
            : sitemap.OrderBy!;

        var includeReplies = sitemap.IncludeReplies || routeData?.IncludeReplies == true;
        var includeForwards = sitemap.IncludeForwards;
        if (routeData?.IncludeForwards == false)
            includeForwards = false;

        var orderDesc = sitemap.OrderDesc;
        if (routeData?.OrderDesc is not null)
            orderDesc = routeData.OrderDesc.Value;

        return new EffectiveSitemapOptions
        {
            ItemLimit = itemLimit,
            OrderBy = orderBy,
            OrderDesc = orderDesc,
            IncludeReplies = includeReplies,
            IncludeForwards = includeForwards,
            Query = query,
            IncludeHome = sitemap.IncludeHome,
            IncludeRoutePaths = sitemap.IncludeRoutePaths,
            PublisherIds = publisherIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Types = types
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList(),
            Categories = categories
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList(),
            Tags = tags
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList(),
        };
    }

    private async Task<List<string>> LoadManifestRoutePathsAsync(Guid siteId)
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
            if (manifest is null)
                continue;

            return manifest.Routes
                .Select(x => x.Path)
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x) &&
                    !x.Contains('{') &&
                    !x.Contains('}') &&
                    !x.Contains('*') &&
                    !x.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !x.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                )
                .Select(NormalizePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    private static string BuildPostPath(SnPost post, PublicationSiteSitemapConfig sitemap)
    {
        var pattern = string.IsNullOrWhiteSpace(sitemap.PostUrlPattern) ? "/posts/{slug}" : sitemap.PostUrlPattern!;
        var slugOrId = post.Slug is { Length: > 0 } ? post.Slug : post.Id.ToString();
        var path = pattern
            .Replace("{slug}", slugOrId, StringComparison.Ordinal)
            .Replace("{id}", post.Id.ToString(), StringComparison.Ordinal);

        return NormalizePath(path);
    }

    private async Task<List<SnPost>> FetchPostsForPublisherAsync(string publisherId, EffectiveSitemapOptions options)
    {
        var posts = new List<SnPost>();
        var pageToken = "0";
        var guard = 0;

        while (guard++ < 600 && posts.Count < options.ItemLimit && !string.IsNullOrWhiteSpace(pageToken))
        {
            var listRequest = new DyListPostsRequest
            {
                PublisherId = publisherId,
                OrderBy = options.OrderBy,
                OrderDesc = options.OrderDesc,
                PageSize = Math.Min(100, Math.Max(1, options.ItemLimit - posts.Count)),
                PageToken = pageToken,
                IncludeReplies = options.IncludeReplies,
            };

            foreach (var type in options.Types)
            {
                var normalized = type.Trim().ToLowerInvariant();
                if (normalized == "article")
                    listRequest.Types_.Add(DyPostType.DyArticle);
                else if (normalized == "moment")
                    listRequest.Types_.Add(DyPostType.DyMoment);
            }

            if (listRequest.Types_.Count == 0)
                listRequest.Types_.Add(DyPostType.DyArticle);

            foreach (var category in options.Categories)
                listRequest.Categories.Add(category);

            foreach (var tag in options.Tags)
                listRequest.Tags.Add(tag);

            if (!string.IsNullOrWhiteSpace(options.Query))
                listRequest.Query = options.Query;

            var response = await postClient.ListPostsAsync(listRequest);
            if (response?.Posts is not null)
                posts.AddRange(response.Posts.Select(SnPost.FromProtoValue));

            if (string.IsNullOrWhiteSpace(response?.NextPageToken))
                break;

            pageToken = response.NextPageToken;
        }

        return posts;
    }

    private static DateTimeOffset GetOrderKey(SnPost post, string? orderBy)
    {
        var normalized = (orderBy ?? "published_at").Trim().ToLowerInvariant();
        return normalized switch
        {
            "updated_at" or "updated" => post.EditedAt?.ToDateTimeOffset()
                                         ?? post.UpdatedAt.ToDateTimeOffset(),
            "created_at" or "date" => post.CreatedAt.ToDateTimeOffset(),
            _ => post.PublishedAt?.ToDateTimeOffset()
                 ?? post.CreatedAt.ToDateTimeOffset(),
        };
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        if (!path.StartsWith('/'))
            path = "/" + path;

        return path;
    }

    private static string ResolveBaseUrl(SnPublicationSite site, HttpContext context)
    {
        var configured = site.Config.BaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (Uri.TryCreate(configured, UriKind.Absolute, out var uri))
                return uri.GetLeftPart(UriPartial.Authority);
        }

        return $"{context.Request.Scheme}://{context.Request.Host}";
    }

    private class EffectiveSitemapOptions
    {
        public int ItemLimit { get; init; }
        public string OrderBy { get; init; } = "published_at";
        public bool OrderDesc { get; init; }
        public bool IncludeReplies { get; init; }
        public bool IncludeForwards { get; init; }
        public bool IncludeHome { get; init; }
        public bool IncludeRoutePaths { get; init; }
        public string? Query { get; init; }
        public List<string> PublisherIds { get; init; } = [];
        public List<string> Types { get; init; } = [];
        public List<string> Categories { get; init; } = [];
        public List<string> Tags { get; init; } = [];
    }
}
