using DysonNetwork.Shared.Models;
using DysonNetwork.Zone.Publication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DysonNetwork.Zone.Pages;

public class IndexModel(AppDatabase db, PublicationSiteManager psm) : PageModel
{
    public SnPublicationSite? Site { get; set; }
    public string? SiteName { get; set; }
    public string? CurrentPath { get; set; }

    public async Task<IActionResult> OnGet(string? path = null)
    {
        var siteNameValue = Request.Headers["X-SiteName"].ToString();
        SiteName = siteNameValue;
        CurrentPath = Path.Combine("/", path ?? "");
        if (string.IsNullOrEmpty(siteNameValue))
        {
            SiteName = null;
            return Page();
        }

        var capturedName = siteNameValue;
        Site = await db.PublicationSites.FirstOrDefaultAsync(s => s.Slug == capturedName);
        if (Site == null) return Page();
        var pagePath = CurrentPath;

        var page = await db.PublicationPages.FirstOrDefaultAsync(p => p.SiteId == Site.Id && p.Path == pagePath);
        if (page != null)
        {
            switch (page.Type)
            {
                case PublicationPageType.HtmlPage
                    when page.Config.TryGetValue("html", out var html) && html is JsonElement content:
                    return Content(content.ToString(), "text/html");
                case PublicationPageType.Redirect
                    when page.Config.TryGetValue("url", out var url) && url is JsonElement redirectUrl:
                    return Redirect(redirectUrl.ToString());
            }
        }

        // If the site is enabled the self-managed mode, try lookup the files then
        if (Site.Mode == PublicationSiteMode.SelfManaged)
        {
            // TODO: Fix the path contains a wwwroot
            var provider = new FileExtensionContentTypeProvider();
            var hostedFilePath = psm.GetValidatedFullPath(Site.Id, CurrentPath);
            if (System.IO.File.Exists(hostedFilePath))
            {
                if (!provider.TryGetContentType(hostedFilePath, out var mimeType))
                    mimeType = "text/html";
                return File(hostedFilePath, mimeType);
            }

            var hostedNotFoundPath = psm.GetValidatedFullPath(Site.Id, "404.html");
            if (System.IO.File.Exists(hostedNotFoundPath))
                return File(hostedNotFoundPath, "text/html");
        }

        return Page();
    }
}