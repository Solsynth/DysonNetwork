using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Zone.Publication;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DysonNetwork.Zone.Pages;

public class PostsModel(PostService.PostServiceClient postClient, RemotePublisherService rps) : PageModel
{
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

        var response = await postClient.ListPostsAsync(request);

        if (response?.Posts != null)
        {
            Posts = response.Posts.Select(SnPost.FromProtoValue).ToList();
            TotalCount = response.TotalSize;
        }
    }
}