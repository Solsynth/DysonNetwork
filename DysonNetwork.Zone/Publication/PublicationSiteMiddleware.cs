using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Zone.Pages.Dynamic;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Zone.Publication;

public class PublicationSiteMiddleware(RequestDelegate next)
{
    public const string SiteContextKey = "PubSite";

    public async Task InvokeAsync(
        HttpContext context,
        AppDatabase db,
        PublicationSiteManager psm,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempData
    )
    {
        var siteNameValue = context.Request.Headers["X-SiteName"].ToString();
        var currentPath = context.Request.Path.Value ?? "";

        if (string.IsNullOrEmpty(siteNameValue))
        {
            await next(context);
            return;
        }

        var site = await db.PublicationSites
            .FirstOrDefaultAsync(s => EF.Functions.ILike(s.Slug, siteNameValue)
            );
        if (site == null)
        {
            await next(context);
            return;
        }

        context.Items[SiteContextKey] = site;

        var page = await db.PublicationPages
            .FirstOrDefaultAsync(p => p.SiteId == site.Id && p.Path == currentPath);
        if (page != null)
        {
            switch (page.Type)
            {
                case PublicationPageType.HtmlPage
                    when page.Config.TryGetValue("html", out var html) && html is JsonElement content:
                    if (site.Mode == PublicationSiteMode.FullyManaged)
                    {
                        context.Items["PublicationHtmlContent"] = content.ToString();
                        var layoutedHtml = await RenderViewAsync(
                            context,
                            viewEngine,
                            tempData,
                            "/Pages/Dynamic/DynamicPage.cshtml",
                            new DynamicPage { Html = content.ToString() }
                        );
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(layoutedHtml);
                    }
                    else
                    {
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(content.ToString());
                    }
                    return;
                case PublicationPageType.Redirect
                    when page.Config.TryGetValue("target", out var tgt) && tgt is JsonElement redirectUrl:
                    context.Response.Redirect(redirectUrl.ToString());
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // If the site is enabled the self-managed mode, try lookup the files then
        if (site.Mode == PublicationSiteMode.SelfManaged)
        {
            var provider = new FileExtensionContentTypeProvider();
            var hostedFilePath = psm.GetValidatedFullPath(site.Id, currentPath);
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

            var hostedNotFoundPath = psm.GetValidatedFullPath(site.Id, "404.html");
            if (File.Exists(hostedNotFoundPath))
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(hostedNotFoundPath);
                return;
            }
        }

        await next(context);
    }

    private async Task<string> RenderViewAsync(
        HttpContext context,
        IRazorViewEngine engine,
        ITempDataProvider tempDataProvider,
        string viewPath,
        object model)
    {
        var endpointFeature = context.Features.Get<IEndpointFeature>();
        var endpoint = endpointFeature?.Endpoint;

        var routeData = context.GetRouteData();

        var actionContext = new ActionContext(
            context,
            routeData,
            new ActionDescriptor()
        );

        await using var sw = new StringWriter();

        var viewResult = engine.GetView(null, viewPath, true);

        if (!viewResult.Success)
            throw new FileNotFoundException($"View '{viewPath}' not found.");

        var viewData = new ViewDataDictionary(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };

        var tempData = new TempDataDictionary(context, tempDataProvider);

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            tempData,
            sw,
            new HtmlHelperOptions()
        );

        await viewResult.View.RenderAsync(viewContext);
        return sw.ToString();
    }
}