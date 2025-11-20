using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Zone.Pages;

public class IndexModel(AppDatabase db) : PageModel
{
    public string? SiteName { get; set; }

    public async Task<IActionResult> OnGet(string? path = null)
    {
        var siteNameValue = Request.Headers["X-SiteName"].ToString();
        SiteName = siteNameValue;
        if (string.IsNullOrEmpty(siteNameValue))
        {
            SiteName = null;
        }
        else
        {
            var capturedName = siteNameValue;
            var site = await db.PublicationSites.FirstOrDefaultAsync(s =>
                s.Name == capturedName && s.Mode == PublicationSiteMode.FullyManaged);
            if (site != null)
            {
                var pagePath = "/" + (path ?? "");
                if (pagePath == "/") pagePath = "/";

                var page = await db.PublicationPages.FirstOrDefaultAsync(p =>
                    p.SiteId == site.Id && p.Path == pagePath);
                if (page != null)
                {
                    if (page.Type == PublicationPageType.HtmlPage)
                    {
                        if (page.Config.TryGetValue("html", out var html) && html is string content)
                        {
                            return Content(content, "text/html");
                        }
                    }
                    else if (page.Type == PublicationPageType.Redirect)
                    {
                        if (page.Config.TryGetValue("url", out var url) && url is string redirectUrl)
                        {
                            return Redirect(redirectUrl);
                        }
                    }
                }
            }
        }

        return Page();
    }
}

