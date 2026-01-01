using System.Text;
using DysonNetwork.Shared.Models;
using Markdig;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubObjectFactory(IConfiguration configuration, AppDatabase db)
{
    public static readonly string PublicTo = "https://www.w3.org/ns/activitystreams#Public";

    public async Task<SnFediverseActor?> GetLocalActorAsync(Guid publisherId)
    {
        return await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.PublisherId == publisherId);
    }

    public Dictionary<string, object> CreatePostObject(
        SnPost post,
        string actorUrl
    )
    {
        var baseDomain = configuration["ActivityPub:Domain"] ?? "localhost";
        var assetsBaseUrl = configuration["ActivityPub:FileBaseUrl"] ?? $"https://{baseDomain}/files";
        var postUrl = $"https://{baseDomain}/posts/{post.Id}";

        // Build content by combining title, description, and main content
        var contentBuilder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(post.Title))
            contentBuilder.Append($"# {post.Title}\n\n");

        if (!string.IsNullOrWhiteSpace(post.Description))
            contentBuilder.Append($"{post.Description}\n\n");

        if (!string.IsNullOrWhiteSpace(post.Content))
            contentBuilder.Append(post.Content);

        // Ensure content is not empty for ActivityPub compatibility
        if (contentBuilder.Length == 0)
            contentBuilder.Append("Shared media");

        if (post.Tags.Count > 0)
        {
            contentBuilder.Append("\n\n");
            contentBuilder.Append(
                string.Join(' ', post.Tags.Select(x => $"#{x.Slug}"))
            );
        }

        var finalContent = contentBuilder.ToString();

        var postReceivers = new List<string> { PublicTo };

        if (post.RepliedPost != null)
        {
            // Local post
            if (post.RepliedPost.Publisher != null)
            {
                var actor = GetLocalActorAsync(post.RepliedPost.PublisherId!.Value).GetAwaiter().GetResult();
                if (actor?.FollowersUri != null)
                    postReceivers.Add(actor.FollowersUri);
            }

            // Fediverse post
            if (post.Actor?.FollowersUri != null)
                postReceivers.Add(post.Actor.FollowersUri);
        }

        var postObject = new Dictionary<string, object>
        {
            ["id"] = postUrl,
            ["type"] = post.Type == PostType.Article ? "Article" : "Note",
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["attributedTo"] = actorUrl,
            ["content"] = Markdown.ToHtml(finalContent),
            ["to"] = postReceivers,
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["attachment"] = post.Attachments.Select(a => new Dictionary<string, object>
            {
                ["type"] = "Document",
                ["mediaType"] = a.MimeType ?? "media/jpeg",
                ["url"] = $"{assetsBaseUrl}/{a.Id}"
            }).ToList<object>()
        };

        if (post.RepliedPost != null)
        {
            // Local post
            if (post.RepliedPost.Publisher != null)
                postObject["inReplyTo"] = $"https://{baseDomain}/posts/{post.RepliedPostId}";
            // Fediverse post
            if (post.RepliedPost.FediverseUri != null)
                postObject["inReplyTo"] = post.RepliedPost.FediverseUri;
        }

        if (post.ForwardedPost != null)
        {
            // Local post
            if (post.ForwardedPost.Publisher != null)
                postObject["quoteUri"] = $"https://{baseDomain}/posts/{post.RepliedPostId}";
            // Fediverse post
            if (post.ForwardedPost.FediverseUri != null)
                postObject["quoteUri"] = post.ForwardedPost.FediverseUri;
        }

        if (post.EditedAt.HasValue)
            postObject["updated"] = post.EditedAt.Value.ToDateTimeOffset();

        return postObject;
    }
}