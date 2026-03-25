using System.Globalization;
using AngleSharp;
using AngleSharp.Dom;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models.Embed;

namespace DysonNetwork.Insight.Reader;

/// <summary>
/// The service is amin to providing scrapping service to the Solar Network.
/// Such as news feed, external articles and link preview.
/// </summary>
public class WebReaderService(
    IHttpClientFactory httpClientFactory,
    ILogger<WebReaderService> logger,
    ICacheService cache
)
{
    private const string LinkPreviewCachePrefix = "scrap:preview:";
    private const string LinkPreviewCacheGroup = "scrap:preview";

    public async Task<ScrapedArticle> ScrapeArticleAsync(string url, CancellationToken cancellationToken = default)
    {
        var linkEmbed = await GetLinkPreviewAsync(url, cancellationToken);
        var httpClient = httpClientFactory.CreateClient("WebReader");
        var content = await FetchArticleContentAsync(httpClient, url, cancellationToken);
        return new ScrapedArticle
        {
            LinkEmbed = linkEmbed,
            Content = content
        };
    }

    private static async Task<string?> FetchArticleContentAsync(
        HttpClient httpClient, string url, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(req => req.Content(html), cancellationToken);
        return document.QuerySelector("article")?.InnerHtml;
    }


    /// <summary>
    /// Generate a link preview embed from a URL
    /// </summary>
    /// <param name="url">The URL to generate the preview for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="bypassCache">If true, bypass cache and fetch fresh data</param>
    /// <param name="cacheExpiry">Custom cache expiration time</param>
    /// <returns>A LinkEmbed object containing the preview data</returns>
    public async Task<LinkEmbed> GetLinkPreviewAsync(
        string url,
        CancellationToken cancellationToken = default,
        TimeSpan? cacheExpiry = null,
        bool bypassCache = false
    )
    {
        // Ensure URL is valid
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException(@"Invalid URL format", nameof(url));
        }

        // Try to get from cache if not bypassing
        if (!bypassCache)
        {
            var cachedPreview = await GetCachedLinkPreview(url);
            if (cachedPreview is not null)
                return cachedPreview;
        }

        // Cache miss or bypass, fetch fresh data
        logger.LogDebug("Fetching fresh link preview for URL: {Url}", url);
        var httpClient = httpClientFactory.CreateClient("WebReader");

        try
        {
            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (contentType == null || !contentType.StartsWith("text/html"))
            {
                logger.LogWarning("URL is not an HTML page: {Url}, ContentType: {ContentType}", url, contentType);
                var nonHtmlEmbed = new LinkEmbed
                {
                    Url = url,
                    Title = uri.Host,
                    ContentType = contentType
                };

                // Cache non-HTML responses too
                await CacheLinkPreview(nonHtmlEmbed, url, cacheExpiry);
                return nonHtmlEmbed;
            }

            // Use final URL after redirects for extraction
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            var finalUri = response.RequestMessage?.RequestUri ?? uri;

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var linkEmbed = await ExtractLinkData(finalUrl, html, finalUri);

            // Cache the result
            await CacheLinkPreview(linkEmbed, url, cacheExpiry);

            return linkEmbed;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Failed to fetch URL: {Url}", url);
            throw new WebReaderException($"Failed to fetch URL: {url}", ex);
        }
    }

    private async Task<LinkEmbed> ExtractLinkData(string url, string html, Uri uri)
    {
        var embed = new LinkEmbed
        {
            Url = url
        };

        // Configure AngleSharp context
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var document = await context.OpenAsync(req => req.Content(html));

        // Extract JSON-LD structured data as an additional fallback source
        var jsonLd = ExtractJsonLdData(document);

        // Extract OpenGraph tags
        var ogTitle = GetMetaTagContent(document, "og:title");
        var ogDescription = GetMetaTagContent(document, "og:description");
        var ogImage = GetMetaTagContent(document, "og:image");
        var ogSiteName = GetMetaTagContent(document, "og:site_name");
        var ogType = GetMetaTagContent(document, "og:type");

        // Extract Twitter card tags as fallback
        var twitterTitle = GetMetaTagContent(document, "twitter:title");
        var twitterDescription = GetMetaTagContent(document, "twitter:description");
        var twitterImage = GetMetaTagContent(document, "twitter:image");

        // Extract standard meta tags as final fallback
        var metaTitle = GetMetaTagContent(document, "title") ??
                        GetMetaContent(document, "title");
        var metaDescription = GetMetaTagContent(document, "description");

        // Extract page title
        var pageTitle = document.Title?.Trim();

        // Extract h1 as title fallback
        var h1Title = document.QuerySelector("h1")?.TextContent?.Trim();

        // Extract first meaningful paragraph as description fallback
        var firstParagraph = GetFirstMeaningfulParagraph(document);

        // Extract first meaningful image as image fallback
        var firstImage = GetFirstMeaningfulImage(document, uri);

        // Extract publish date
        var publishedTime = GetMetaTagContent(document, "article:published_time") ??
                            GetMetaTagContent(document, "datePublished") ??
                            GetMetaTagContent(document, "pubdate");

        // Extract author
        var author = GetMetaTagContent(document, "author") ??
                     GetMetaTagContent(document, "article:author");

        // Extract favicon
        var faviconUrl = GetFaviconUrl(document, uri);

        // Check for canonical URL (useful after redirects)
        var canonicalUrl = document.QuerySelector("link[rel='canonical'][href]")
            ?.GetAttribute("href")?.Trim();
        if (!string.IsNullOrEmpty(canonicalUrl) && Uri.TryCreate(canonicalUrl, UriKind.Absolute, out var canonicalUri))
        {
            embed.Url = canonicalUri.ToString();
            uri = canonicalUri;
        }

        // Populate the embed, prioritizing: OG -> Twitter -> JSON-LD -> meta -> DOM -> hostname
        embed.Title = ogTitle ?? twitterTitle ?? jsonLd.Title ?? metaTitle ?? pageTitle ?? h1Title ?? uri.Host;
        embed.Description = ogDescription ?? twitterDescription ?? jsonLd.Description ?? metaDescription ?? firstParagraph;
        embed.ImageUrl = ResolveRelativeUrl(ogImage ?? twitterImage ?? jsonLd.ImageUrl ?? firstImage, uri);
        embed.SiteName = ogSiteName ?? jsonLd.SiteName ?? GetMetaContent(document, "application-name") ?? uri.Host;
        embed.ContentType = ogType ?? jsonLd.Type;
        embed.FaviconUrl = faviconUrl;
        embed.Author = author ?? jsonLd.Author;

        // Parse and set published date
        if (!string.IsNullOrEmpty(publishedTime) &&
            DateTime.TryParse(publishedTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal,
                out var parsedDate))
        {
            embed.PublishedDate = parsedDate;
        }

        return embed;
    }

    private static JsonLdData ExtractJsonLdData(IDocument document)
    {
        var result = new JsonLdData();
        var scriptNodes = document.QuerySelectorAll("script[type='application/ld+json']");

        foreach (var script in scriptNodes)
        {
            var jsonText = script.TextContent;
            if (string.IsNullOrWhiteSpace(jsonText)) continue;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);
                var root = doc.RootElement;

                // Handle arrays of JSON-LD objects
                var elements = root.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? root.EnumerateArray()
                    : Enumerable.Repeat(root, 1);

                foreach (var element in elements)
                {
                    if (string.IsNullOrEmpty(result.Title))
                        result.Title = GetStringProperty(element, "headline") ??
                                      GetStringProperty(element, "name");

                    if (string.IsNullOrEmpty(result.Description))
                        result.Description = GetStringProperty(element, "description");

                    if (string.IsNullOrEmpty(result.ImageUrl))
                        result.ImageUrl = ExtractImageFromJsonLd(element);

                    if (string.IsNullOrEmpty(result.SiteName))
                    {
                        var publisher = GetNestedProperty(element, "publisher");
                        if (publisher != null)
                            result.SiteName = GetStringProperty(publisher.Value, "name");
                    }

                    if (string.IsNullOrEmpty(result.Author))
                    {
                        var authorProp = element.TryGetProperty("author", out var authorEl) ? authorEl : default;
                        if (authorProp.ValueKind == System.Text.Json.JsonValueKind.Object)
                            result.Author = GetStringProperty(authorProp, "name");
                        else if (authorProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                            result.Author = authorProp.EnumerateArray().FirstOrDefault()
                                .TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                    }

                    if (string.IsNullOrEmpty(result.Type))
                        result.Type = GetStringProperty(element, "@type");

                    // Stop after finding useful data
                    if (!string.IsNullOrEmpty(result.Title) || !string.IsNullOrEmpty(result.Description))
                        break;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Skip malformed JSON-LD
            }
        }

        return result;
    }

    private static string? GetStringProperty(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) && prop.ValueKind == System.Text.Json.JsonValueKind.String
            ? prop.GetString()
            : null;

    private static System.Text.Json.JsonElement? GetNestedProperty(System.Text.Json.JsonElement element, string name) =>
        element.TryGetProperty(name, out var prop) ? prop : null;

    private static string? ExtractImageFromJsonLd(System.Text.Json.JsonElement element)
    {
        if (element.TryGetProperty("image", out var image))
        {
            if (image.ValueKind == System.Text.Json.JsonValueKind.String)
                return image.GetString();
            if (image.ValueKind == System.Text.Json.JsonValueKind.Array)
                return image.EnumerateArray().FirstOrDefault().GetString();
            if (image.ValueKind == System.Text.Json.JsonValueKind.Object)
                return GetStringProperty(image, "url");
        }
        return null;
    }

    private static string? GetFirstMeaningfulParagraph(IDocument document)
    {
        foreach (var p in document.QuerySelectorAll("article p, main p, .content p, .post p, p"))
        {
            var text = p.TextContent?.Trim();
            if (!string.IsNullOrEmpty(text) && text.Length > 30 && text.Length < 500)
                return text.Length > 300 ? string.Concat(text.AsSpan(0, 297), "...") : text;
        }
        return null;
    }

    private static string? GetFirstMeaningfulImage(IDocument document, Uri baseUri)
    {
        foreach (var img in document.QuerySelectorAll("article img, main img, .content img, img"))
        {
            var src = img.GetAttribute("src");
            if (string.IsNullOrEmpty(src)) continue;
            if (src.StartsWith("data:")) continue;

            // Skip tiny tracking pixels and icons
            var width = img.GetAttribute("width");
            var height = img.GetAttribute("height");
            if (int.TryParse(width, out var w) && int.TryParse(height, out var h) && w < 50 && h < 50)
                continue;

            return src;
        }
        return null;
    }

    private static string? GetMetaTagContent(IDocument doc, string property)
    {
        // Check for OpenGraph/Twitter style meta tags
        var node = doc.QuerySelector($"meta[property='{property}'][content]")
                   ?? doc.QuerySelector($"meta[name='{property}'][content]");

        return node?.GetAttribute("content")?.Trim();
    }

    private static string? GetMetaContent(IDocument doc, string name)
    {
        var node = doc.QuerySelector($"meta[name='{name}'][content]");
        return node?.GetAttribute("content")?.Trim();
    }

    private static string? GetFaviconUrl(IDocument doc, Uri baseUri)
    {
        // Look for apple-touch-icon first as it's typically higher quality
        var appleIconNode = doc.QuerySelector("link[rel='apple-touch-icon'][href]");
        if (appleIconNode != null)
        {
            return ResolveRelativeUrl(appleIconNode.GetAttribute("href"), baseUri);
        }

        // Then check for standard favicon
        var faviconNode = doc.QuerySelector("link[rel='icon'][href]") ??
                          doc.QuerySelector("link[rel='shortcut icon'][href]");

        return faviconNode != null
            ? ResolveRelativeUrl(faviconNode.GetAttribute("href"), baseUri)
            : new Uri(baseUri, "/favicon.ico").ToString();
    }

    private static string? ResolveRelativeUrl(string? url, Uri baseUri)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return url; // Already absolute
        }

        return Uri.TryCreate(baseUri, url, out var absoluteUri) ? absoluteUri.ToString() : null;
    }

    /// <summary>
    /// Generate a hash-based cache key for a URL
    /// </summary>
    private string GenerateUrlCacheKey(string url)
    {
        // Normalize the URL first
        var normalizedUrl = NormalizeUrl(url);

        // Create SHA256 hash of the normalized URL
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var urlBytes = System.Text.Encoding.UTF8.GetBytes(normalizedUrl);
        var hashBytes = sha256.ComputeHash(urlBytes);

        // Convert to hex string
        var hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        // Return prefixed key
        return $"{LinkPreviewCachePrefix}{hashString}";
    }

    /// <summary>
    /// Normalize URL by trimming trailing slashes but preserving query parameters
    /// </summary>
    private string NormalizeUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        // First ensure we have a valid URI
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url.TrimEnd('/');

        // Rebuild the URL without trailing slashes but with query parameters
        var scheme = uri.Scheme;
        var host = uri.Host;
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";
        var path = uri.AbsolutePath.TrimEnd('/');
        var query = uri.Query;

        return $"{scheme}://{host}{port}{path}{query}".ToLowerInvariant();
    }

    /// <summary>
    /// Cache a link preview
    /// </summary>
    private async Task CacheLinkPreview(LinkEmbed? linkEmbed, string url, TimeSpan? expiry = null)
    {
        if (linkEmbed == null || string.IsNullOrEmpty(url))
            return;

        try
        {
            var cacheKey = GenerateUrlCacheKey(url);
            var expiryTime = expiry ?? TimeSpan.FromHours(24);

            await cache.SetWithGroupsAsync(
                cacheKey,
                linkEmbed,
                [LinkPreviewCacheGroup],
                expiryTime);

            logger.LogDebug("Cached link preview for URL: {Url} with key: {CacheKey}", url, cacheKey);
        }
        catch (Exception ex)
        {
            // Log but don't throw - caching failures shouldn't break the main functionality
            logger.LogWarning(ex, "Failed to cache link preview for URL: {Url}", url);
        }
    }

    /// <summary>
    /// Try to get a cached link preview
    /// </summary>
    private async Task<LinkEmbed?> GetCachedLinkPreview(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        try
        {
            var cacheKey = GenerateUrlCacheKey(url);
            var cachedPreview = await cache.GetAsync<LinkEmbed>(cacheKey);

            if (cachedPreview is not null)
                logger.LogDebug("Retrieved cached link preview for URL: {Url}", url);

            return cachedPreview;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve cached link preview for URL: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Invalidate cache for a specific URL
    /// </summary>
    public async Task InvalidateCacheForUrlAsync(string url)
    {
        if (string.IsNullOrEmpty(url))
            return;

        try
        {
            var cacheKey = GenerateUrlCacheKey(url);
            await cache.RemoveAsync(cacheKey);
            logger.LogDebug("Invalidated cache for URL: {Url} with key: {CacheKey}", url, cacheKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to invalidate cache for URL: {Url}", url);
        }
    }

    /// <summary>
    /// Invalidate all cached link previews
    /// </summary>
    public async Task InvalidateAllCachedPreviewsAsync()
    {
        try
        {
            await cache.RemoveGroupAsync(LinkPreviewCacheGroup);
            logger.LogInformation("Invalidated all cached link previews");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to invalidate all cached link previews");
        }
    }

    private record JsonLdData
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? SiteName { get; set; }
        public string? Author { get; set; }
        public string? Type { get; set; }
    }
}