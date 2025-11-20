using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.IO;
using System.Text.Json;

namespace DysonNetwork.Zone.Pages;

public class IndexModel(AppDatabase db) : PageModel
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
        }
        else
        {
            var capturedName = siteNameValue;
            Site = await db.PublicationSites.FirstOrDefaultAsync(s => s.Slug == capturedName);
            if (Site == null) return Page();
            var pagePath = CurrentPath;

            var page = await db.PublicationPages.FirstOrDefaultAsync(p => p.SiteId == Site.Id && p.Path == pagePath);
            if (page == null) return Page();
            switch (page.Type)
            {
                case PublicationPageType.HtmlPage when page.Config.TryGetValue("html", out var html) && html is JsonElement content:
                    return Content(content.ToString(), "text/html");
                case PublicationPageType.Redirect when page.Config.TryGetValue("url", out var url) && url is JsonElement redirectUrl:
                    return Redirect(redirectUrl.ToString());
            }
        }

        return Page();
    }
}
