using System.Diagnostics;
using DysonNetwork.Shared.Models;
using DysonNetwork.Zone.Publication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Diagnostics;

namespace DysonNetwork.Zone.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    [FromRoute] public new int? StatusCode { get; set; }
    
    public SnPublicationSite? Site { get; set; }
    public string? SiteName { get; set; }
    public string? CurrentPath { get; set; }
    public string? OriginalPath { get; set; }

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        if (HttpContext.Items.TryGetValue(PublicationSiteMiddleware.SiteContextKey, out var site))
            Site = site as SnPublicationSite;

        SiteName = Request.Headers["X-SiteName"].ToString();
        CurrentPath = Request.Path;

        var statusCodeReExecuteFeature = HttpContext.Features.Get<IStatusCodeReExecuteFeature>();
        OriginalPath = statusCodeReExecuteFeature?.OriginalPath;
    }
}