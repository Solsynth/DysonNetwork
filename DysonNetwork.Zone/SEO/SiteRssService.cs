using System.ServiceModel.Syndication;
using System.Xml;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Zone.Publication;

namespace DysonNetwork.Zone.SEO;

public class SiteRssService(
    DyPostService.DyPostServiceClient postClient,
    MarkdownConverter markdownConverter,
    TemplateRouteResolver routeResolver
)
{
    public async Task<string?> RenderRssIfMatched(HttpContext context, SnPublicationSite site)
    {
        var rss = site.Config.Rss;
        if (rss is not { Enabled: true })
            return null;

        var requestPath = NormalizePath(context.Request.Path.Value ?? "/");
        var rssPath = NormalizePath(string.IsNullOrWhiteSpace(rss.Path) ? "/rss.xml" : rss.Path!);
        if (!string.Equals(requestPath, rssPath, StringComparison.OrdinalIgnoreCase))
            return null;

        var routeData = await ResolveRouteDataAsync(site, rss);
        var options = BuildEffectiveOptions(site, rss, routeData);
        var baseUrl = ResolveBaseUrl(site, context);

        var feed = new SyndicationFeed(
            string.IsNullOrWhiteSpace(rss.Title) ? $"{site.Name} RSS" : rss.Title,
            string.IsNullOrWhiteSpace(rss.Description) ? (site.Description ?? string.Empty) : rss.Description,
            new Uri(baseUrl + "/")
        );

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

        var feedPosts = filtered.Take(options.ItemLimit).ToList();

        var items = new List<SyndicationItem>();
        foreach (var post in feedPosts)
        {
            var postUrl = BuildPostUrl(baseUrl, post, rss);

            var title = string.IsNullOrWhiteSpace(post.Title)
                ? (string.IsNullOrWhiteSpace(post.Description) ? "Untitled" : post.Description)
                : post.Title;

            var content = BuildContent(post, rss.ContentMode);

            var updated = post.EditedAt?.ToDateTimeOffset()
                          ?? post.PublishedAt?.ToDateTimeOffset()
                          ?? post.CreatedAt.ToDateTimeOffset();

            var item = new SyndicationItem(
                title,
                content,
                new Uri(postUrl),
                post.Id.ToString(),
                updated
            )
            {
                PublishDate = post.PublishedAt?.ToDateTimeOffset() ?? post.CreatedAt.ToDateTimeOffset()
            };

            items.Add(item);
        }

        feed.Items = items;

        await using var sw = new StringWriter();
        await using var writer = XmlWriter.Create(sw, new XmlWriterSettings
        {
            Indent = true,
            Async = true,
        });

        var formatter = new Rss20FeedFormatter(feed);
        formatter.WriteTo(writer);
        await writer.FlushAsync();

        return sw.ToString();
    }

    private async Task<TemplateRouteDataOptions?> ResolveRouteDataAsync(
        SnPublicationSite site,
        PublicationSiteRssConfig rss
    )
    {
        if (string.IsNullOrWhiteSpace(rss.SourceRoutePath))
            return null;

        try
        {
            var route = await routeResolver.ResolveAsync(site, rss.SourceRoutePath!);
            return route.RouteEntry?.Data;
        }
        catch
        {
            return null;
        }
    }

    private static EffectiveRssOptions BuildEffectiveOptions(
        SnPublicationSite site,
        PublicationSiteRssConfig rss,
        TemplateRouteDataOptions? routeData
    )
    {
        var itemLimit = Math.Clamp(rss.ItemLimit <= 0 ? 20 : rss.ItemLimit, 1, 100);

        var publisherIds = rss.PublisherIds;
        if (publisherIds.Count == 0 && routeData?.PublisherIds is { Count: > 0 })
            publisherIds = routeData.PublisherIds;
        if (publisherIds.Count == 0)
            publisherIds = [site.PublisherId.ToString()];

        var types = rss.Types;
        if (types.Count == 0 && routeData?.Types is { Count: > 0 })
            types = routeData.Types;
        if (types.Count == 0)
            types = ["article"];

        var categories = rss.Categories;
        if (categories.Count == 0 && routeData?.Categories is { Count: > 0 })
            categories = routeData.Categories;

        var tags = rss.Tags;
        if (tags.Count == 0 && routeData?.Tags is { Count: > 0 })
            tags = routeData.Tags;

        var query = string.IsNullOrWhiteSpace(rss.Query)
            ? routeData?.Query
            : rss.Query;

        var orderBy = string.IsNullOrWhiteSpace(rss.OrderBy)
            ? (string.IsNullOrWhiteSpace(routeData?.OrderBy) ? "published_at" : routeData!.OrderBy!)
            : rss.OrderBy!;

        var includeReplies = rss.IncludeReplies || routeData?.IncludeReplies == true;
        var includeForwards = rss.IncludeForwards;
        if (routeData?.IncludeForwards == false)
            includeForwards = false;

        var orderDesc = rss.OrderDesc;
        if (routeData?.OrderDesc is not null)
            orderDesc = routeData.OrderDesc.Value;

        return new EffectiveRssOptions
        {
            ItemLimit = itemLimit,
            OrderBy = orderBy,
            OrderDesc = orderDesc,
            IncludeReplies = includeReplies,
            IncludeForwards = includeForwards,
            Query = query,
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

    private string BuildContent(SnPost post, string? mode)
    {
        var normalized = (mode ?? "excerpt").Trim().ToLowerInvariant();

        if (normalized == "none")
            return string.Empty;

        if (normalized == "html")
        {
            if (!string.IsNullOrWhiteSpace(post.Content))
                return markdownConverter.ToHtml(post.Content!, softBreaks: post.Type != DysonNetwork.Shared.Models.PostType.Article);

            return post.Description ?? string.Empty;
        }

        return post.Description
            ?? (!string.IsNullOrWhiteSpace(post.Content)
                ? TrimTo(post.Content!, 220)
                : string.Empty);
    }

    private async Task<List<SnPost>> FetchPostsForPublisherAsync(string publisherId, EffectiveRssOptions options)
    {
        var posts = new List<SnPost>();
        var targetRawCount = Math.Clamp(options.ItemLimit * 3, options.ItemLimit, 2000);
        var pageToken = "0";
        var guard = 0;

        while (guard++ < 200 && posts.Count < targetRawCount && !string.IsNullOrWhiteSpace(pageToken))
        {
            var listRequest = new ListPostsRequest
            {
                PublisherId = publisherId,
                OrderBy = options.OrderBy,
                OrderDesc = options.OrderDesc,
                PageSize = Math.Min(100, Math.Max(1, targetRawCount - posts.Count)),
                PageToken = pageToken,
                IncludeReplies = options.IncludeReplies,
            };

            foreach (var type in options.Types)
            {
                var normalized = type.Trim().ToLowerInvariant();
                if (normalized == "article")
                    listRequest.Types_.Add(Shared.Proto.PostType.Article);
                else if (normalized == "moment")
                    listRequest.Types_.Add(Shared.Proto.PostType.Moment);
            }

            if (listRequest.Types_.Count == 0)
                listRequest.Types_.Add(Shared.Proto.PostType.Article);

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

    private static string BuildPostUrl(string baseUrl, SnPost post, PublicationSiteRssConfig rss)
    {
        var pattern = string.IsNullOrWhiteSpace(rss.PostUrlPattern) ? "/posts/{slug}" : rss.PostUrlPattern!;

        var slugOrId = post.Slug is { Length: > 0 } ? post.Slug : post.Id.ToString();
        var path = pattern
            .Replace("{slug}", slugOrId, StringComparison.Ordinal)
            .Replace("{id}", post.Id.ToString(), StringComparison.Ordinal);

        if (!path.StartsWith('/'))
            path = "/" + path;

        return $"{baseUrl}{path}";
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

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        if (!path.StartsWith('/'))
            path = "/" + path;

        return path;
    }

    private static string TrimTo(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength] + "...";
    }

    private class EffectiveRssOptions
    {
        public int ItemLimit { get; init; }
        public string OrderBy { get; init; } = "published_at";
        public bool OrderDesc { get; init; }
        public bool IncludeReplies { get; init; }
        public bool IncludeForwards { get; init; }
        public string? Query { get; init; }
        public List<string> PublisherIds { get; init; } = [];
        public List<string> Types { get; init; } = [];
        public List<string> Categories { get; init; } = [];
        public List<string> Tags { get; init; } = [];
    }
}
