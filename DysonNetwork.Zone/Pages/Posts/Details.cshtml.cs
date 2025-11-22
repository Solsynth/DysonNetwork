using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Zone.Publication;
// Add this using statement
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PostType = DysonNetwork.Shared.Proto.PostType;

namespace DysonNetwork.Zone.Pages.Posts;

public class DetailsModel(PostService.PostServiceClient postClient, MarkdownConverter markdownConverter) : PageModel
{
    [FromRoute] public string Slug { get; set; } = null!;

    public SnPublicationSite? Site { get; set; }
    public SnPost? Post { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Site = HttpContext.Items[PublicationSiteMiddleware.SiteContextKey] as SnPublicationSite;

        if (string.IsNullOrEmpty(Slug))
            return NotFound();

        var request = new GetPostRequest { PublisherId = Site!.PublisherId.ToString() };
        if (Guid.TryParse(Slug, out var guid)) request.Id = guid.ToString();
        else request.Slug = Slug;
        var response = await postClient.GetPostAsync(request);

        if (response == null)
        {
            return NotFound();
        }

        Post = SnPost.FromProtoValue(response);

        // Convert the markdown content to HTML
        if (Post != null && !string.IsNullOrEmpty(Post.Content))
            Post.Content = markdownConverter.ToHtml(Post.Content,
                softBreaks: Post.Type != DysonNetwork.Shared.Models.PostType.Article);

        return Page();
    }
}