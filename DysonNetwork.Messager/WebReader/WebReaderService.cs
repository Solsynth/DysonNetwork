using System.Globalization;
using AngleSharp;
using AngleSharp.Dom;
using DysonNetwork.Shared.Cache;
using HtmlAgilityPack;

namespace DysonNetwork.Messager.WebReader;

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
        var content = await GetArticleContentAsync(url, cancellationToken);
        return new ScrapedArticle
        {
            LinkEmbed = linkEmbed,
            Content = content
        };
    }

    private async Task<string?> GetArticleContentAsync(string url, CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient("WebReader");
        var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to scrap article content for URL: {Url}", url);
            return null;
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var articleNode = doc.DocumentNode.SelectSingleNode("//article");
        return articleNode?.InnerHtml;
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
        httpClient.MaxResponseContentBufferSize =
            10 * 1024 * 1024; // 10MB, prevent scrap some directly accessible files
        httpClient.Timeout = TimeSpan.FromSeconds(3);
        // Setting UA to facebook's bot to get the opengraph.
        httpClient.DefaultRequestHeaders.Add("User-Agent", "facebookexternalhit/1.1");

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

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var linkEmbed = await ExtractLinkData(url, html, uri);

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

        // Extract publish date
        var publishedTime = GetMetaTagContent(document, "article:published_time") ??
                            GetMetaTagContent(document, "datePublished") ??
                            GetMetaTagContent(document, "pubdate");

        // Extract author
        var author = GetMetaTagContent(document, "author") ??
                     GetMetaTagContent(document, "article:author");

        // Extract favicon
        var faviconUrl = GetFaviconUrl(document, uri);

        // Populate the embed with the data, prioritizing OpenGraph
        embed.Title = ogTitle ?? twitterTitle ?? metaTitle ?? pageTitle ?? uri.Host;
        embed.Description = ogDescription ?? twitterDescription ?? metaDescription;
        embed.ImageUrl = ResolveRelativeUrl(ogImage ?? twitterImage, uri);
        embed.SiteName = ogSiteName ?? uri.Host;
        embed.ContentType = ogType;
        embed.FaviconUrl = faviconUrl;
        embed.Author = author;

        // Parse and set published date
        if (!string.IsNullOrEmpty(publishedTime) &&
            DateTime.TryParse(publishedTime, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal,
                out DateTime parsedDate))
        {
            embed.PublishedDate = parsedDate;
        }

        return embed;
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
}