using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Zone.Customization;
using DysonNetwork.Zone.Publication;
using Microsoft.AspNetCore.Mvc;
// Add this using statement
using Microsoft.AspNetCore.Mvc.RazorPages;
using PostType = DysonNetwork.Shared.Models.PostType;

namespace DysonNetwork.Zone.Pages;

public class PostsModel(
    DyPostService.DyPostServiceClient postClient,
    RemotePublisherService rps,
    MarkdownConverter markdownConverter
) : PageModel
{
    public SnPublicationSite? Site { get; set; }
    public SnPublisher? Publisher { get; set; }
    public List<SnPost> Posts { get; set; } = [];
    public int TotalCount { get; set; }

    public int Index { get; set; }
    public int PageSize { get; set; } = 10;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public PostPageFilterConfig? FilterConfig { get; set; }
    public PostPageLayoutConfig? LayoutConfig { get; set; }

    public async Task OnGetAsync(int index = 1)
    {
        FilterConfig = HttpContext.Items["PostPage_FilterConfig"] as PostPageFilterConfig;
        LayoutConfig = HttpContext.Items["PostPage_LayoutConfig"] as PostPageLayoutConfig;
        Site = HttpContext.Items[PublicationSiteMiddleware.SiteContextKey] as SnPublicationSite;
        Index = index;

        Publisher = FilterConfig?.PubName is not null
            ? await rps.GetPublisher(FilterConfig.PubName)
            : await rps.GetPublisher(id: Site!.PublisherId.ToString());

        var request = new ListPostsRequest
        {
            OrderBy = FilterConfig?.OrderBy,
            OrderDesc = FilterConfig?.OrderDesc ?? true,
            PageSize = PageSize,
            PageToken = ((Index - 1) * PageSize).ToString(),
            PublisherId = Publisher!.Id.ToString()
        };

        if (FilterConfig?.Types is not null)
        {
            foreach (var type in FilterConfig.Types)
            {
                request.Types_.Add(type switch
                {
                    0 => DysonNetwork.DyPostType.Moment,
                    1 => DysonNetwork.DyPostType.Article,
                    _ => DysonNetwork.DyPostType.Unspecified,
                });
            }
        }
        else
        {
            request.Types_.Add(DysonNetwork.DyPostType.Article);
        }

        var response = await postClient.ListPostsAsync(request);

        if (response?.Posts != null)
        {
            Posts = response.Posts.Select(SnPost.FromProtoValue).ToList();
            TotalCount = response.TotalSize;

            // Convert the markdown content to HTML
            foreach (var post in Posts.Where(post => !string.IsNullOrEmpty(post.Content)))
                post.Content = markdownConverter.ToHtml(post.Content!, softBreaks: post.Type != PostType.Article);
        }
    }
}