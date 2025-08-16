using System.Net;
using DysonNetwork.Shared.PageData;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using OpenGraphNet;

namespace DysonNetwork.Sphere.PageData;

public class PostPageData(
    AppDatabase db,
    AccountService.AccountServiceClient accounts,
    Publisher.PublisherService pub,
    PostService ps,
    IConfiguration configuration
)
    : IPageDataProvider
{
    private readonly string _siteUrl = configuration["SiteUrl"]!;

    public bool CanHandlePath(PathString path) =>
        path.StartsWithSegments("/posts");

    public async Task<IDictionary<string, object?>> GetAppDataAsync(HttpContext context)
    {
        var path = context.Request.Path.Value!;
        var startIndex = "/posts/".Length;
        var endIndex = path.IndexOf('/', startIndex);
        var slug = endIndex == -1 ? path[startIndex..] : path.Substring(startIndex, endIndex - startIndex);
        slug = WebUtility.UrlDecode(slug);

        var postId = Guid.TryParse(slug, out var postIdGuid) ? postIdGuid : Guid.Empty;
        if (postId == Guid.Empty) return new Dictionary<string, object?>();

        context.Items.TryGetValue("CurrentUser", out var currentUserValue);
        var currentUser = currentUserValue as Account;
        List<Guid> userFriends = [];
        if (currentUser != null)
        {
            var friendsResponse = await accounts.ListFriendsAsync(new ListRelationshipSimpleRequest
            { AccountId = currentUser.Id });
            userFriends = friendsResponse.AccountsId.Select(Guid.Parse).ToList();
        }

        var userPublishers = currentUser is null ? [] : await pub.GetUserPublishers(Guid.Parse(currentUser.Id));

        var post = await db.Posts
            .Where(e => e.Id == postId)
            .Include(e => e.Publisher)
            .Include(e => e.Tags)
            .Include(e => e.Categories)
            .FilterWithVisibility(currentUser, userFriends, userPublishers)
            .FirstOrDefaultAsync();
        post = await ps.LoadPostInfo(post, currentUser);

        // Track view - use the account ID as viewer ID if user is logged in
        await ps.IncreaseViewCount(post.Id, currentUser?.Id);

        var og = OpenGraph.MakeGraph(
            title: post.Title ?? $"Post from {post.Publisher.Name}",
            type: "article",
            image: $"{_siteUrl}/cgi/drive/files/{post.Publisher.Background?.Id}?original=true",
            url: $"{_siteUrl}/@{slug}",
            description: post.Description ?? post.Content?[..80] ?? "Posted with some media",
            siteName: "Solar Network"
        );

        return new Dictionary<string, object?>()
        {
            ["Post"] = post,
            ["OpenGraph"] = og
        };
    }
}
