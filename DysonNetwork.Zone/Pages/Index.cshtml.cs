using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Zone.Publication;
// Add this using statement
using Microsoft.AspNetCore.Mvc.RazorPages;
using NodaTime;
using PostType = DysonNetwork.DyPostType;

namespace DysonNetwork.Zone.Pages;

public class IndexModel(
    DyPostService.DyPostServiceClient postClient,
    RemotePublisherService rps,
    RemoteAccountService ras,
    MarkdownConverter markdownConverter
) : PageModel
{
    public SnPublicationSite? Site { get; set; }
    public SnPublisher? Publisher { get; set; }
    public Account? UserAccount { get; set; }
    public List<SnPost> FeaturedPosts { get; set; } = [];

    public string? UserPictureUrl =>
        UserAccount?.Profile?.Picture?.Id != null
            ? $"/drive/files/{UserAccount.Profile.Picture.Id}"
            : null;

    public string? UserBackgroundUrl =>
        UserAccount?.Profile?.Background?.Id != null
            ? $"/drive/files/{UserAccount.Profile.Background.Id}?original=true"
            : null;

    public async Task OnGetAsync()
    {
        Site = HttpContext.Items[PublicationSiteMiddleware.SiteContextKey] as SnPublicationSite;

        if (Site != null)
        {
            // Fetch Publisher Information
            Publisher = await rps.GetPublisher(id: Site!.PublisherId.ToString());

            // Fetch User Account Information if available
            UserAccount = await ras.GetAccount(Site.AccountId);

            // Fetch Featured Posts (e.g., top 5 by views)
            var request = new ListPostsRequest
            {
                OrderBy = "popularity",
                OrderDesc = true,
                PageSize = 5,
                PublisherId = Site!.PublisherId.ToString(),
            };
            
            request.Types_.Add(PostType.Article);

            var response = await postClient.ListPostsAsync(request);

            if (response?.Posts != null)
            {
                FeaturedPosts = response.Posts.Select(SnPost.FromProtoValue).ToList();

                // Convert the markdown content to HTML
                foreach (
                    var post in FeaturedPosts.Where(post => !string.IsNullOrEmpty(post.Content))
                )
                    post.Content = markdownConverter.ToHtml(post.Content!);
            }
        }
    }

    public int CalculateAge(Instant birthday)
    {
        var birthDate = birthday.ToDateTimeOffset();
        var today = DateTimeOffset.Now;
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
            age--;
        return age;
    }

    public string GetOffsetUtcString(string targetTimeZone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(targetTimeZone);
            var offset = tz.GetUtcOffset(DateTimeOffset.Now);
            var sign = offset >= TimeSpan.Zero ? "+" : "-";
            return $"{Math.Abs(offset.Hours):D2}:{Math.Abs(offset.Minutes):D2}";
        }
        catch
        {
            return "00:00";
        }
    }

    public string GetCurrentTimeInTimeZone(string targetTimeZone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(targetTimeZone);
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, tz);
            return now.ToString("t", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return DateTime.Now.ToString("t", System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public (string Name, string Color) GetPerkInfo(string identifier)
    {
        return identifier switch
        {
            "solian.stellar.primary" => ("Stellar", "#2196f3"),
            "solian.stellar.nova" => ("Nova", "#39c5bb"),
            "solian.stellar.supernova" => ("Supernova", "#ffc109"),
            _ => ("Unknown", "#2196f3"),
        };
    }
}

