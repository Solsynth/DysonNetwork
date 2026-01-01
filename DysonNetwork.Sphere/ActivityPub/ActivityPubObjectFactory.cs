using System.Text;
using DysonNetwork.Shared.Models;
using Markdig;

namespace DysonNetwork.Sphere.ActivityPub;

public static class ActivityPubObjectFactory
{
    public static readonly string[] PublicTo = ["https://www.w3.org/ns/activitystreams#Public"];

    public static Dictionary<string, object> CreatePostObject(
        IConfiguration configuration,
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

        var postObject = new Dictionary<string, object>
        {
            ["id"] = postUrl,
            ["type"] = post.Type == PostType.Article ? "Article" : "Note",
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["attributedTo"] = actorUrl,
            ["content"] = Markdown.ToHtml(finalContent),
            ["to"] = PublicTo,
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["attachment"] = post.Attachments.Select(a => new Dictionary<string, object>
            {
                ["type"] = "Document",
                ["mediaType"] = a.MimeType ?? "media/jpeg",
                ["url"] = $"{assetsBaseUrl}/{a.Id}"
            }).ToList<object>()
        };

        if (post.EditedAt.HasValue)
            postObject["updated"] = post.EditedAt.Value.ToDateTimeOffset();

        return postObject;
    }
}