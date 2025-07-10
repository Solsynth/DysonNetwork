using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Pages.Posts;

public class PostDetailModel(
    AppDatabase db,
    PublisherService pub,
    RelationshipService rels       
) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid PostId { get; set; }

    public Post.Post? Post { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (PostId == Guid.Empty)
            return NotFound();
            
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Sphere.Account.Account;
        var userFriends = currentUser is null ? [] : await rels.ListAccountFriends(currentUser);
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(currentUser.Id);

        Post = await db.Posts
            .Where(e => e.Id == PostId)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();

        if (Post == null)
            return NotFound();

        return Page();
    }
}