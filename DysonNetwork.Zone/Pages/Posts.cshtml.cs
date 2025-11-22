using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Zone.Publication;
using Microsoft.AspNetCore.Mvc;
// Add this using statement
using Microsoft.AspNetCore.Mvc.RazorPages;
using PostType = DysonNetwork.Shared.Models.PostType;

namespace DysonNetwork.Zone.Pages;

public class PostsModel(
    PostService.PostServiceClient postClient,
    RemotePublisherService rps,
    MarkdownConverter markdownConverter
) : PageModel
{
    [FromQuery] public bool ShowAll { get; set; } = false;

    public SnPublicationSite? Site { get; set; }
    public SnPublisher? Publisher { get; set; }
    public List<SnPost> Posts { get; set; } = [];
    public int TotalCount { get; set; }

    public int CurrentPage { get; set; }
    public int PageSize { get; set; } = 10;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);

    public async Task OnGetAsync(int currentPage = 1)
    {
        Site = HttpContext.Items[PublicationSiteMiddleware.SiteContextKey] as SnPublicationSite;
        CurrentPage = currentPage;

        Publisher = await rps.GetPublisher(id: Site!.PublisherId.ToString());

        var request = new ListPostsRequest
        {
            OrderBy = "date",
            OrderDesc = true,
            PageSize = PageSize,
            PageToken = ((CurrentPage - 1) * PageSize).ToString(),
            PublisherId = Site!.PublisherId.ToString()
        };

        if (!ShowAll) request.Types_.Add(DysonNetwork.Shared.Proto.PostType.Article);

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