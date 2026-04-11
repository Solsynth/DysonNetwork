using System.Text;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Markdig;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubObjectFactory(IConfiguration configuration, IDbContextFactory<AppDatabase> dbFactory, RemoteRealmService? realmService = null)
{
    public static readonly string PublicTo = "https://www.w3.org/ns/activitystreams#Public";

    private readonly IDbContextFactory<AppDatabase> _dbFactory = dbFactory;

    private AppDatabase CreateDbContext() => _dbFactory.CreateDbContext();

    public async Task<SnFediverseActor?> GetLocalActorAsync(Guid publisherId)
    {
        using var db = CreateDbContext();
        return await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.PublisherId == publisherId);
    }

    public async Task<Dictionary<string, object>> CreatePostObject(
        SnPost post,
        string actorUrl
    )
    {
        using var db = CreateDbContext();
        
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

        var postReceivers = new List<string> { $"{actorUrl}/followers" };

        if (post.RepliedPostId != null)
        {
            var repliedPost = await db.Posts
                .Where(p => p.Id == post.RepliedPostId)
                .Include(p => p.Publisher)
                .Include(p => p.Actor)
                .FirstOrDefaultAsync();
            post.RepliedPost = repliedPost;

            // Local post
            if (repliedPost?.Publisher != null)
            {
                var actor = await GetLocalActorAsync(repliedPost.PublisherId!.Value);
                if (actor?.FollowersUri != null)
                    postReceivers.Add(actor.FollowersUri);
            }

            // Fediverse post
            if (repliedPost?.Actor?.FollowersUri != null)
                postReceivers.Add(post.Actor!.FollowersUri!);
        }

        var attachments = post.Attachments.Select(a =>
        {
            var type = a.MimeType?.Split('/')[0] switch
            {
                "image" => "Image",
                "video" => "Video",
                "audio" => "Audio",
                _ => "Document"
            };

            var attachment = new Dictionary<string, object>
            {
                ["type"] = type,
                ["mediaType"] = a.MimeType ?? "application/octet-stream",
                ["url"] = $"{assetsBaseUrl}/{a.Id}"
            };

            if (a.Width.HasValue) attachment["width"] = a.Width.Value;
            if (a.Height.HasValue) attachment["height"] = a.Height.Value;
            if (a.Size > 0) attachment["size"] = a.Size;
            if (!string.IsNullOrEmpty(a.Blurhash)) attachment["blurhash"] = a.Blurhash;

            return attachment;
        }).ToList<object>();

        var postObject = new Dictionary<string, object>
        {
            ["id"] = postUrl,
            ["type"] = post.Type == PostType.Article ? "Article" : "Note",
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["attributedTo"] = actorUrl,
            ["content"] = Markdown.ToHtml(finalContent),
            ["to"] = new[] { PublicTo },
            ["cc"] = postReceivers,
            ["attachment"] = attachments
        };

        if (post.RealmId.HasValue && realmService != null)
        {
            try
            {
                var realm = await realmService.GetRealm(post.RealmId.Value.ToString());
                if (realm != null && realm.IsCommunity)
                {
                    postObject["audience"] = $"https://{baseDomain}/activitypub/realms/{realm.Slug}";
                }
            }
            catch
            {
            }
        }

        // The post's replied post is ensure loaded above, so we directly using it here
        if (post.RepliedPost != null)
        {
            // Local post
            if (post.RepliedPost.Publisher != null)
                postObject["inReplyTo"] = $"https://{baseDomain}/posts/{post.RepliedPostId}";
            // Fediverse post
            if (post.RepliedPost.FediverseUri != null)
                postObject["inReplyTo"] = post.RepliedPost.FediverseUri;
        }

        if (post.ForwardedPostId != null)
        {
            var forwardedPost = await db.Posts
                .Where(p => p.Id == post.ForwardedPostId)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();

            // Local post
            if (forwardedPost?.Publisher != null)
            {
                var quoteUrl = $"https://{baseDomain}/posts/{post.ForwardedPostId}";
                postObject["quote"] = quoteUrl;
                postObject["quoteUrl"] = quoteUrl;
                postObject["quoteUri"] = quoteUrl;
            }
            // Fediverse post
            if (forwardedPost?.FediverseUri != null)
            {
                postObject["quote"] = forwardedPost.FediverseUri;
                postObject["quoteUrl"] = forwardedPost.FediverseUri;
                postObject["quoteUri"] = forwardedPost.FediverseUri;
            }

            // Add interactionPolicy for the quoted post
            if (forwardedPost != null)
            {
                var postAuthor = forwardedPost.Publisher != null
                    ? await GetLocalActorAsync(forwardedPost.PublisherId!.Value)
                    : forwardedPost.Actor;

                if (postAuthor != null)
                {
                    postObject["@context"] = new object[]
                    {
                        "https://www.w3.org/ns/activitystreams",
                        new Dictionary<string, object>
                        {
                            ["gts"] = "https://gotosocial.org/ns#",
                            ["interactionPolicy"] = new Dictionary<string, object>
                            {
                                ["@id"] = "gts:interactionPolicy",
                                ["@type"] = "@id"
                            },
                            ["canQuote"] = new Dictionary<string, object>
                            {
                                ["@id"] = "gts:canQuote",
                                ["@type"] = "@id"
                            },
                            ["automaticApproval"] = new Dictionary<string, object>
                            {
                                ["@id"] = "gts:automaticApproval",
                                ["@type"] = "@id"
                            }
                        }
                    };

                    var quotePolicy = new Dictionary<string, object>
                    {
                        ["canQuote"] = new Dictionary<string, object>
                        {
                            ["automaticApproval"] = "https://www.w3.org/ns/activitystreams#Public"
                        }
                    };
                    postObject["interactionPolicy"] = quotePolicy;
                }
            }
        }

        // Add quoteAuthorization if exists
        if (post.QuoteAuthorizationId.HasValue)
        {
            var quoteAuth = await db.QuoteAuthorizations
                .FirstOrDefaultAsync(q => q.Id == post.QuoteAuthorizationId && q.IsValid);

            if (quoteAuth != null)
            {
                var authUrl = $"https://{baseDomain}/quote-authorizations/{quoteAuth.Id}";
                postObject["quoteAuthorization"] = authUrl;
            }
        }

        if (post.EditedAt.HasValue)
            postObject["updated"] = post.EditedAt.Value.ToDateTimeOffset();

        return postObject;
    }
}