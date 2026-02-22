using DysonNetwork.Shared.Models;
using DysonNetwork.Zone.SEO;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Zone.Publication;

public class PublicationSiteMiddleware(RequestDelegate next)
{
    public const string SiteContextKey = "PubSite";

    public async Task InvokeAsync(
        HttpContext context,
        AppDatabase db,
        PublicationSiteManager siteManager,
        TemplateSiteRenderer templateRenderer,
        SiteRssService rssService
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
                context.Response.ContentType = renderResult.ContentType;

                if (!string.IsNullOrWhiteSpace(renderResult.StaticFilePath))
                {
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
}
