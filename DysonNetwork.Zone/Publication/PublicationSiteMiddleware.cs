using DysonNetwork.Shared.Models;
using DysonNetwork.Zone.SEO;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace DysonNetwork.Zone.Publication;

public class PublicationSiteMiddleware(RequestDelegate next)
{
    public const string SiteContextKey = "PubSite";

    public async Task InvokeAsync(
        HttpContext context,
        AppDatabase db,
        PublicationSiteManager siteManager,
        TemplateSiteRenderer templateRenderer,
        SiteRssService rssService,
        SiteSitemapService sitemapService
    )
    {
        var siteNameValue = context.Request.Headers["X-SiteName"].ToString();
        var currentPath = context.Request.Path.Value ?? "/";

        if (string.IsNullOrWhiteSpace(siteNameValue))
        {
            await next(context);
            return;
        }

        var site = await db.PublicationSites
            .FirstOrDefaultAsync(s => EF.Functions.ILike(s.Slug, siteNameValue));
        if (site == null)
        {
            await next(context);
            return;
        }

        context.Items[SiteContextKey] = site;

        if (site.Mode == PublicationSiteMode.FullyManaged)
        {
            var sitemapContent = await sitemapService.RenderSitemapIfMatched(context, site);
            if (sitemapContent is not null)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/xml; charset=utf-8";
                await context.Response.WriteAsync(sitemapContent);
                return;
            }

            var rssContent = await rssService.RenderRssIfMatched(context, site);
            if (rssContent is not null)
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "application/rss+xml; charset=utf-8";
                await context.Response.WriteAsync(rssContent);
                return;
            }

            var renderResult = await templateRenderer.RenderAsync(context, site);
            if (renderResult.Handled)
            {
                context.Response.StatusCode = renderResult.StatusCode;

                if (!string.IsNullOrWhiteSpace(renderResult.RedirectLocation))
                {
                    context.Response.Headers.Location = renderResult.RedirectLocation;
                    return;
                }

                context.Response.ContentType = renderResult.ContentType;

                if (!string.IsNullOrWhiteSpace(renderResult.StaticFilePath))
                {
                    if (await TryWriteMinifiedAssetAsync(
                            context,
                            renderResult.StaticFilePath,
                            renderResult.ContentType,
                            site.Config.AutoMinifyAssets
                        ))
                    {
                        return;
                    }

                    await context.Response.SendFileAsync(renderResult.StaticFilePath);
                    return;
                }

                if (renderResult.Content is not null)
                {
                    await context.Response.WriteAsync(renderResult.Content);
                    return;
                }
            }
        }

        if (site.Mode == PublicationSiteMode.SelfManaged)
        {
            var provider = new FileExtensionContentTypeProvider();
            var hostedFilePath = siteManager.GetValidatedFullPath(site.Id, currentPath);
            if (File.Exists(hostedFilePath))
            {
                if (!provider.TryGetContentType(hostedFilePath, out var mimeType))
                    mimeType = "text/html";

                context.Response.ContentType = mimeType;

                if (await TryWriteMinifiedAssetAsync(
                        context,
                        hostedFilePath,
                        mimeType,
                        site.Config.AutoMinifyAssets
                    ))
                {
                    return;
                }

                await context.Response.SendFileAsync(hostedFilePath);
                return;
            }

            if (Directory.Exists(hostedFilePath))
            {
                var indexPath = Path.Combine(hostedFilePath, "index.html");
                if (File.Exists(indexPath))
                {
                    context.Response.ContentType = "text/html";
                    await context.Response.SendFileAsync(indexPath);
                    return;
                }
            }

            var hostedNotFoundPath = siteManager.GetValidatedFullPath(site.Id, "404.html");
            if (File.Exists(hostedNotFoundPath))
            {
                context.Response.ContentType = "text/html";
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.SendFileAsync(hostedNotFoundPath);
                return;
            }
        }

        await next(context);
    }

    private static async Task<bool> TryWriteMinifiedAssetAsync(
        HttpContext context,
        string fullPath,
        string contentType,
        bool autoMinifyAssets
    )
    {
        if (!autoMinifyAssets)
            return false;

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext != ".css" && ext != ".js")
            return false;

        // Skip files that are already minified by naming convention.
        var fileName = Path.GetFileName(fullPath);
        if (fileName.Contains(".min.", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var source = await File.ReadAllTextAsync(fullPath);
            var minified = ext == ".css" ? MinifyCss(source) : MinifyJs(source);

            context.Response.ContentType = contentType.Contains("charset=", StringComparison.OrdinalIgnoreCase)
                ? contentType
                : $"{contentType}; charset=utf-8";
            await context.Response.WriteAsync(minified, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string MinifyCss(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        var withoutComments = System.Text.RegularExpressions.Regex.Replace(
            source,
            @"/\*[\s\S]*?\*/",
            string.Empty
        );

        var compact = System.Text.RegularExpressions.Regex.Replace(withoutComments, @"\s+", " ");
        compact = System.Text.RegularExpressions.Regex.Replace(compact, @"\s*([{}:;,>+~])\s*", "$1");
        compact = compact.Replace(";}", "}", StringComparison.Ordinal);

        return compact.Trim();
    }

    private static string MinifyJs(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return string.Empty;

        // Conservative JS minification: remove comments and collapse whitespace,
        // while preserving string/template/regex-like literals.
        var sb = new StringBuilder(source.Length);
        var inSingle = false;
        var inDouble = false;
        var inTemplate = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escape = false;

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                    sb.Append(' ');
                }
                continue;
            }

            if (inBlockComment)
            {
                if (ch == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }
                continue;
            }

            if (!inSingle && !inDouble && !inTemplate && ch == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (!inSingle && !inDouble && !inTemplate && ch == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (!escape)
            {
                if (!inDouble && !inTemplate && ch == '\'')
                    inSingle = !inSingle;
                else if (!inSingle && !inTemplate && ch == '"')
                    inDouble = !inDouble;
                else if (!inSingle && !inDouble && ch == '`')
                    inTemplate = !inTemplate;
            }

            if (!inSingle && !inDouble && !inTemplate && char.IsWhiteSpace(ch))
            {
                if (sb.Length == 0 || char.IsWhiteSpace(sb[^1]))
                    continue;
                sb.Append(' ');
                continue;
            }

            sb.Append(ch);

            if (escape)
                escape = false;
            else if (ch == '\\' && (inSingle || inDouble || inTemplate))
                escape = true;
        }

        var compact = sb.ToString();
        compact = System.Text.RegularExpressions.Regex.Replace(
            compact,
            @"\s*([{}();,:<>\+\-\*/=\[\]])\s*",
            "$1"
        );
        return compact.Trim();
    }
}
