using System.Text.RegularExpressions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Zone.Publication;

public class TemplateContextBuilder(
    PostService.PostServiceClient postClient,
    RemotePublisherService publisherService,
    MarkdownConverter markdownConverter
)
{
    public async Task<Dictionary<string, object?>> BuildAsync(
        SnPublicationSite site,
        TemplateRouteResolution route,
        HttpContext httpContext
    )
    {
        var publisher = await publisherService.GetPublisher(id: site.PublisherId.ToString());

        var pageIndex = ParsePositiveInt(httpContext.Request.Query["index"].ToString())
                        ?? ParsePositiveInt(httpContext.Request.Query["page"].ToString())
                        ?? 1;

        var pageSize = Math.Clamp(route.RouteEntry?.Data?.PageSize ?? 10, 1, 50);
        var mode = ResolveDataMode(route, httpContext.Request.Path.Value ?? "/");

        var pageObject = new Dictionary<string, object?>();
        var postsObject = new Dictionary<string, object?>();
        Dictionary<string, object?>? postObject = null;

        if (mode == "post_detail")
        {
            var detail = await BuildPostDetailAsync(site, route, pageSize, pageIndex);
            pageObject = detail.Page;
            postObject = detail.Post;
            postsObject = detail.Posts;
        }
        else
        {
            var listing = await BuildPostListingAsync(site, route, pageSize, pageIndex);
            pageObject = listing.Page;
            postsObject = listing.Posts;
            postObject = listing.CurrentPost;
        }

        var effectivePageType = string.IsNullOrWhiteSpace(route.PageType) ? "page" : route.PageType;
        if (mode == "post_detail" && effectivePageType == "page")
            effectivePageType = "post";

        var scheme = httpContext.Request.Scheme;
        var host = httpContext.Request.Host.Value;
        var baseUrl = $"{scheme}://{host}";

        var theme = BuildDefaultTheme();

        return new Dictionary<string, object?>
        {
            ["site"] = new Dictionary<string, object?>
            {
                ["id"] = site.Id.ToString(),
                ["slug"] = site.Slug,
                ["name"] = site.Name,
                ["description"] = site.Description,
                ["mode"] = site.Mode.ToString(),
                ["publisher_id"] = site.PublisherId.ToString(),
                ["config"] = site.Config,
            },
            ["publisher"] = publisher,
            ["route"] = new Dictionary<string, object?>
            {
                ["path"] = httpContext.Request.Path.Value ?? "/",
                ["query"] = httpContext.Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString()),
                ["params"] = route.RouteParams,
                ["index"] = pageIndex,
                ["page"] = pageIndex,
            },
            ["page"] = pageObject,
            ["posts"] = postsObject,
            ["post"] = postObject,
            ["page_type"] = effectivePageType,
            ["asset_url"] = string.Empty,
            ["base_url"] = baseUrl,
            ["config"] = new Dictionary<string, object?>
            {
                ["title"] = site.Name,
                ["description"] = site.Description ?? string.Empty,
                ["url"] = baseUrl,
            },
            ["theme"] = theme,
            ["locale"] = BuildLocaleDictionary(),
            ["now"] = DateTime.UtcNow,
            ["now_iso"] = DateTimeOffset.UtcNow.ToString("O"),
            ["open_graph_tags"] = string.Empty,
            ["feed_tag"] = string.Empty,
            ["favicon_tag"] = string.Empty,
        };
    }

    private async Task<(Dictionary<string, object?> Page, Dictionary<string, object?> Post, Dictionary<string, object?> Posts)>
        BuildPostDetailAsync(SnPublicationSite site, TemplateRouteResolution route, int pageSize, int pageIndex)
    {
        var slugKey = route.RouteEntry?.Data?.SlugParam;
        var lookupSlug = !string.IsNullOrWhiteSpace(slugKey) && route.RouteParams.TryGetValue(slugKey!, out var configured)
            ? configured
            : route.RouteParams.TryGetValue("slug", out var direct)
                ? direct
                : string.Empty;

        if (string.IsNullOrWhiteSpace(lookupSlug))
        {
            var segments = route.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            lookupSlug = segments.LastOrDefault() ?? string.Empty;
        }

        SnPost? post = null;
        if (!string.IsNullOrWhiteSpace(lookupSlug))
        {
            var publisherIds = route.RouteEntry?.Data?.PublisherIds is { Count: > 0 }
                ? route.RouteEntry.Data.PublisherIds
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string> { site.PublisherId.ToString() };

            foreach (var publisherId in publisherIds)
            {
                try
                {
                    var request = new GetPostRequest { PublisherId = publisherId };
                    if (Guid.TryParse(lookupSlug, out var id))
                        request.Id = id.ToString();
                    else
                        request.Slug = lookupSlug;

                    var response = await postClient.GetPostAsync(request);
                    post = response is null ? null : SnPost.FromProtoValue(response);

                    if (post is not null && !string.IsNullOrWhiteSpace(post.Content))
                        post.Content = markdownConverter.ToHtml(
                            post.Content!,
                            softBreaks: post.Type != DysonNetwork.Shared.Models.PostType.Article
                        );

                    if (post is not null)
                        break;
                }
                catch
                {
                    // Try next publisher if this one doesn't contain the post.
                }
            }
        }

        var renderedPost = post is null ? new Dictionary<string, object?>() : ToTemplatePost(post);
        var currentSlug = post?.Slug ?? lookupSlug;

        var page = new Dictionary<string, object?>
        {
            ["title"] = post?.Title,
            ["description"] = post?.Description,
            ["posts"] = post is null ? [] : new List<Dictionary<string, object?>> { renderedPost },
            ["current"] = pageIndex,
            ["total"] = 1,
            ["total_size"] = post is null ? 0 : 1,
            ["prev_link"] = null,
            ["next_link"] = null,
            ["pagination_html"] = string.Empty,
            ["slug"] = currentSlug,
        };

        var posts = new Dictionary<string, object?>
        {
            ["items"] = post is null ? [] : new List<Dictionary<string, object?>> { renderedPost },
            ["current"] = pageIndex,
            ["total"] = 1,
            ["total_size"] = post is null ? 0 : 1,
        };

        return (page, renderedPost, posts);
    }

    private async Task<(Dictionary<string, object?> Page, Dictionary<string, object?>? CurrentPost, Dictionary<string, object?> Posts)>
        BuildPostListingAsync(SnPublicationSite site, TemplateRouteResolution route, int pageSize, int pageIndex)
    {
        var data = route.RouteEntry?.Data;
        var orderDesc = data?.OrderDesc ?? true;
        var orderBy = string.IsNullOrWhiteSpace(data?.OrderBy) ? null : data!.OrderBy;
        var includeReplies = data?.IncludeReplies ?? false;
        var includeForwards = data?.IncludeForwards ?? true;

        var publisherIds = data?.PublisherIds is { Count: > 0 }
            ? data.PublisherIds.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string> { site.PublisherId.ToString() };

        var fetchSize = Math.Clamp(pageSize * pageIndex, pageSize, 200);
        var allPosts = new List<SnPost>();
        var totalSize = 0;

        foreach (var publisherId in publisherIds)
        {
            var request = new ListPostsRequest
            {
                PublisherId = publisherId,
                PageSize = fetchSize,
                PageToken = "0",
                OrderDesc = orderDesc,
                IncludeReplies = includeReplies,
            };

            if (!string.IsNullOrWhiteSpace(orderBy))
                request.OrderBy = orderBy;

            var typeHints = data?.Types;
            if (typeHints is { Count: > 0 })
            {
                foreach (var type in typeHints)
                {
                    var normalized = type.Trim().ToLowerInvariant();
                    if (normalized == "moment")
                        request.Types_.Add(DysonNetwork.Shared.Proto.PostType.Moment);
                    else if (normalized == "article")
                        request.Types_.Add(DysonNetwork.Shared.Proto.PostType.Article);
                }
            }

            if (request.Types_.Count == 0)
                request.Types_.Add(DysonNetwork.Shared.Proto.PostType.Article);

            if (data?.Categories is { Count: > 0 })
                request.Categories.AddRange(data.Categories.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

            if (data?.Tags is { Count: > 0 })
                request.Tags.AddRange(data.Tags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

            if (!string.IsNullOrWhiteSpace(data?.Query))
                request.Query = data.Query;

            var response = await postClient.ListPostsAsync(request);
            totalSize += response.TotalSize;
            allPosts.AddRange(response.Posts.Select(SnPost.FromProtoValue));
        }

        var mergedPosts = allPosts
            .Where(p => includeReplies || p.RepliedPostId == null)
            .Where(p => includeForwards || p.ForwardedPostId == null)
            .GroupBy(p => p.Id)
            .Select(g => g.First())
            .OrderByDescending(GetOrderKey)
            .ToList();

        if (!orderDesc)
            mergedPosts.Reverse();

        var posts = mergedPosts
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        foreach (var post in posts.Where(p => !string.IsNullOrWhiteSpace(p.Content)))
            post.Content = markdownConverter.ToHtml(
                post.Content!,
                softBreaks: post.Type != DysonNetwork.Shared.Models.PostType.Article
            );

        var renderedPosts = posts.Select(ToTemplatePost).ToList();

        totalSize = Math.Max(totalSize, mergedPosts.Count);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalSize / (double)pageSize));
        var prevIndex = pageIndex > 1 ? pageIndex - 1 : 1;
        var nextIndex = pageIndex < totalPages ? pageIndex + 1 : totalPages;

        var page = new Dictionary<string, object?>
        {
            ["title"] = site.Name,
            ["description"] = site.Description,
            ["posts"] = renderedPosts,
            ["current"] = pageIndex,
            ["total"] = totalPages,
            ["total_size"] = totalSize,
            ["prev_link"] = $"/?page={prevIndex}",
            ["next_link"] = $"/?page={nextIndex}",
            ["pagination_html"] = BuildPaginationHtml(pageIndex, totalPages),
        };

        var postsObject = new Dictionary<string, object?>
        {
            ["items"] = renderedPosts,
            ["current"] = pageIndex,
            ["total"] = totalPages,
            ["total_size"] = totalSize,
        };

        return (page, renderedPosts.FirstOrDefault(), postsObject);
    }

    private static DateTimeOffset GetOrderKey(SnPost post)
    {
        return post.PublishedAt?.ToDateTimeOffset()
               ?? post.EditedAt?.ToDateTimeOffset()
               ?? post.CreatedAt.ToDateTimeOffset();
    }

    private static string ResolveDataMode(TemplateRouteResolution route, string requestPath)
    {
        var mode = route.RouteEntry?.Data?.Mode?.Trim().ToLowerInvariant();
        if (mode is "posts_list" or "post_detail" or "none")
            return mode;

        if (route.RouteParams.ContainsKey("slug"))
            return "post_detail";

        if (requestPath.Contains("/posts/", StringComparison.OrdinalIgnoreCase) &&
            !requestPath.EndsWith("/posts", StringComparison.OrdinalIgnoreCase))
        {
            return "post_detail";
        }

        return "posts_list";
    }

    private static Dictionary<string, object?> ToTemplatePost(SnPost post)
    {
        var path = post.Slug is { Length: > 0 } ? $"/posts/{post.Slug}" : $"/posts/{post.Id}";
        var content = post.Content ?? string.Empty;
        var plainText = StripHtml(content);
        var publishedAt = post.PublishedAt?.ToDateTimeOffset() ?? post.CreatedAt.ToDateTimeOffset();
        var createdAt = post.CreatedAt.ToDateTimeOffset();
        var updatedAt = post.EditedAt?.ToDateTimeOffset() ?? post.UpdatedAt.ToDateTimeOffset();

        var photos = post.Attachments
            .Where(a => a.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false)
            .Select(a => string.IsNullOrWhiteSpace(a.Id) ? string.Empty : $"/drive/files/{a.Id}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
        var attachments = post.Attachments
            .Select(a => new Dictionary<string, object?>
            {
                ["id"] = a.Id,
                ["name"] = a.Name,
                ["url"] = string.IsNullOrWhiteSpace(a.Url) ? $"/drive/files/{a.Id}" : a.Url,
                ["mime_type"] = a.MimeType,
                ["size"] = a.Size,
                ["width"] = a.Width,
                ["height"] = a.Height,
                ["is_image"] = a.MimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false,
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["id"] = post.Id.ToString(),
            ["title"] = post.Title ?? string.Empty,
            ["description"] = post.Description ?? string.Empty,
            ["slug"] = post.Slug ?? post.Id.ToString(),
            ["layout"] = "post",
            ["content"] = content,
            ["excerpt"] = post.Description is { Length: > 0 }
                ? post.Description
                : Truncate(plainText, 220),
            ["path"] = path,
            ["url"] = path,
            ["photos"] = photos,
            ["attachments"] = attachments,
            ["word_count"] = CountWords(plainText),
            ["date"] = publishedAt.UtcDateTime,
            ["published_at"] = publishedAt.UtcDateTime,
            ["created_at"] = createdAt.UtcDateTime,
            ["updated_at"] = updatedAt.UtcDateTime,
            ["published_at_iso"] = publishedAt.ToString("O"),
            ["created_at_iso"] = createdAt.ToString("O"),
            ["updated_at_iso"] = updatedAt.ToString("O"),
            ["categories"] = post.Categories.Select(c => new Dictionary<string, object?>
            {
                ["slug"] = c.Slug,
                ["name"] = c.Name,
                ["path"] = $"/categories/{c.Slug}",
            }).ToList(),
            ["tags"] = post.Tags.Select(t => new Dictionary<string, object?>
            {
                ["slug"] = t.Slug,
                ["name"] = t.Name,
                ["path"] = $"/tags/{t.Slug}",
            }).ToList(),
        };
    }

    private static int? ParsePositiveInt(string? value)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private static string BuildPaginationHtml(int current, int total)
    {
        if (total <= 1)
            return string.Empty;

        var links = Enumerable.Range(1, total)
            .Select(i => i == current
                ? $"<span class=\"current\">{i}</span>"
                : $"<a href=\"/?page={i}\">{i}</a>");

        return string.Join(" ", links);
    }

    private static string Truncate(string value, int length)
    {
        if (value.Length <= length)
            return value;
        return value[..length] + "...";
    }

    private static int CountWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        return Regex.Matches(value, @"\b\w+\b").Count;
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        return Regex.Replace(input, "<.*?>", string.Empty);
    }

    private static Dictionary<string, object?> BuildLocaleDictionary()
    {
        return new Dictionary<string, object?>
        {
            ["words"] = "words",
            ["read_more"] = "Read more",
            ["comment"] = "Comments",
            ["archive_a"] = "Archive",
            ["category"] = "Category",
            ["tag"] = "Tag",
        };
    }

    private static Dictionary<string, object?> BuildDefaultTheme()
    {
        return new Dictionary<string, object?>
        {
            ["previewMode"] = false,
            ["rss"] = false,
            ["favicon"] = false,
            ["banner"] = new Dictionary<string, object?>
            {
                ["enable"] = false,
                ["onAllPages"] = false,
            },
            ["sidebar"] = new Dictionary<string, object?>
            {
                ["position"] = "left",
            },
            ["home"] = new Dictionary<string, object?>
            {
                ["style"] = "detail",
            },
            ["comment"] = new Dictionary<string, object?>
            {
                ["valine"] = new Dictionary<string, object?> { ["enable"] = false },
                ["twikoo"] = new Dictionary<string, object?> { ["enable"] = false },
                ["waline"] = new Dictionary<string, object?> { ["enable"] = false },
            },
        };
    }
}
