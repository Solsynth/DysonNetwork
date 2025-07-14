using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Post;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Pages.Posts;

public class PostDetailModel(
    AppDatabase db,
    PublisherService pub,
    AccountService.AccountServiceClient accounts
) : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid PostId { get; set; }

    public Post.Post? Post { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (PostId == Guid.Empty)
            return NotFound();

        HttpContext.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);
        var userFriends = currentUser is null
            ? []
            : (await accounts.ListFriendsAsync(
                new ListRelationshipSimpleRequest { AccountId = currentUser.Id }
            )).AccountsId.Select(Guid.Parse).ToList();
        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(accountId);

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