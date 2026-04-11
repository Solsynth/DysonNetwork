using System.Text;
using DysonNetwork.Shared.ActivityStreams;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Markdig;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class ActivityRenderer(IConfiguration configuration, AppDatabase db)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string FileBaseUrl => configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

    public static readonly string PublicTo = "https://www.w3.org/ns/activitystreams#Public";

    public async Task<SnFediverseActor?> GetLocalActorAsync(Guid publisherId)
    {
        return await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.PublisherId == publisherId);
    }

    public async Task<Dictionary<string, object>> CreatePostObject(
        SnPost post,
        string actorUrl
    )
    {
        var baseDomain = Domain;
        var assetsBaseUrl = FileBaseUrl;
        var postUrl = $"https://{baseDomain}/posts/{post.Id}";

        var contentBuilder = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(post.Title))
            contentBuilder.Append($"# {post.Title}\n\n");

        if (!string.IsNullOrWhiteSpace(post.Description))
            contentBuilder.Append($"{post.Description}\n\n");

        if (!string.IsNullOrWhiteSpace(post.Content))
            contentBuilder.Append(post.Content);

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

            if (repliedPost?.Publisher != null)
            {
                var actor = await GetLocalActorAsync(repliedPost.PublisherId!.Value);
                if (actor?.FollowersUri != null)
                    postReceivers.Add(actor.FollowersUri);
            }

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

        if (post.RepliedPost != null)
        {
            if (post.RepliedPost.Publisher != null)
                postObject["inReplyTo"] = $"https://{baseDomain}/posts/{post.RepliedPostId}";
            if (post.RepliedPost.FediverseUri != null)
                postObject["inReplyTo"] = post.RepliedPost.FediverseUri;
        }

        if (post.ForwardedPostId != null)
        {
            var forwardedPost = await db.Posts
                .Where(p => p.Id == post.ForwardedPostId)
                .Include(p => p.Publisher)
                .FirstOrDefaultAsync();

            if (forwardedPost?.Publisher != null)
            {
                var quoteUrl = $"https://{baseDomain}/posts/{post.ForwardedPostId}";
                postObject["quote"] = quoteUrl;
            }
            if (forwardedPost?.FediverseUri != null)
            {
                postObject["quote"] = forwardedPost.FediverseUri;
            }

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
                            ["canQuote"] = "https://gotosocial.org/ns#canQuote"
                        }
                    };

                    postObject["interactionPolicy"] = new Dictionary<string, object>
                    {
                        ["canQuote"] = new Dictionary<string, object>
                        {
                            ["automaticApproval"] = "https://www.w3.org/ns/activitystreams#Public"
                        }
                    };
                }
            }
        }

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

    public ASPerson RenderPerson(SnFediverseActor actor)
    {
        var person = new ASPerson
        {
            Id = actor.Uri,
            PreferredUsername = actor.Username,
            Name = actor.DisplayName,
            Summary = actor.Bio,
            Inbox = actor.InboxUri,
            Outbox = actor.OutboxUri,
            Followers = actor.FollowersUri,
            Following = actor.FollowingUri,
            Featured = actor.FeaturedUri,
            Url = actor.WebUrl,
            Bot = actor.IsBot,
            Discoverable = actor.IsDiscoverable,
            ManuallyApprovesFollowers = actor.IsLocked
        };

        if (!string.IsNullOrEmpty(actor.PublicKeyId))
        {
            person.PublicKey = new ASPublicKey
            {
                Id = actor.PublicKeyId,
                Owner = actor.Uri,
                PublicKeyPem = actor.PublicKey ?? ""
            };
        }

        if (!string.IsNullOrEmpty(actor.AvatarUrl))
        {
            person.Icon = new ASObject { Id = actor.AvatarUrl, Type = "Image", Url = actor.AvatarUrl };
        }

        if (!string.IsNullOrEmpty(actor.HeaderUrl))
        {
            person.Image = new ASObject { Id = actor.HeaderUrl, Type = "Image", Url = actor.HeaderUrl };
        }

        return person;
    }

    public ASNote RenderNote(SnPost post, string actorUrl)
    {
        var note = new ASNote
        {
            Id = $"https://{Domain}/posts/{post.Id}",
            AttributedTo = actorUrl,
            Content = post.Content,
            Published = post.PublishedAt?.ToDateTimeOffset().DateTime,
            Url = $"https://{Domain}/posts/{post.Id}"
        };

        if (post.EditedAt.HasValue)
            note.Updated = post.EditedAt.Value.ToDateTimeOffset().DateTime;

        if (post.RepliedPostId.HasValue)
        {
            note.InReplyTo = post.RepliedPost?.FediverseUri ?? $"https://{Domain}/posts/{post.RepliedPostId}";
        }

        if (post.Attachments.Count > 0)
        {
            note.Attachment = post.Attachments.Select(RenderAttachment).ToArray();
        }

        return note;
    }

    public ASObject RenderAttachment(ICloudFile attachment)
    {
        var mimeType = attachment.MimeType ?? "application/octet-stream";
        var category = mimeType.Split('/')[0];

        var obj = category switch
        {
            "image" => new ASImage { Url = $"{FileBaseUrl}/{attachment.Id}" },
            "video" => new ASVideo { Url = $"{FileBaseUrl}/{attachment.Id}" },
            "audio" => new ASAudio { Url = $"{FileBaseUrl}/{attachment.Id}" },
            _ => new ASObject { Type = "Document", Url = $"{FileBaseUrl}/{attachment.Id}" }
        };

        obj.Id = $"{FileBaseUrl}/{attachment.Id}";
        obj.MediaType = mimeType;

        if (attachment.FileMeta.TryGetValue("width", out var w) && w is int width)
            obj.Width = width;
        if (attachment.FileMeta.TryGetValue("height", out var h) && h is int height)
            obj.Height = height;
        if (attachment.FileMeta.TryGetValue("blurhash", out var bh) && bh is string blurhash)
            obj.Blurhash = blurhash;

        return obj;
    }

    public ASCreate RenderCreateActivity(string activityId, string actorUrl, ASObject obj)
    {
        return new ASCreate
        {
            Id = activityId,
            Actor = actorUrl,
            Object = obj,
            Published = DateTime.UtcNow
        };
    }

    public ASFollow RenderFollowActivity(string activityId, string actorUrl, string objectUrl)
    {
        return new ASFollow
        {
            Id = activityId,
            Actor = actorUrl,
            Object = objectUrl,
            Published = DateTime.UtcNow
        };
    }

    public ASAccept RenderAcceptActivity(string activityId, string actorUrl, ASFollow follow)
    {
        return new ASAccept
        {
            Id = activityId,
            Actor = actorUrl,
            Object = follow,
            Published = DateTime.UtcNow
        };
    }

    public ASAnnounce RenderAnnounceActivity(string activityId, string actorUrl, string objectUrl)
    {
        return new ASAnnounce
        {
            Id = activityId,
            Actor = actorUrl,
            Object = objectUrl,
            Published = DateTime.UtcNow
        };
    }

    public ASLike RenderLikeActivity(string activityId, string actorUrl, string objectUrl)
    {
        return new ASLike
        {
            Id = activityId,
            Actor = actorUrl,
            Object = objectUrl,
            Published = DateTime.UtcNow
        };
    }

    public ASDelete RenderDeleteActivity(string activityId, string actorUrl, string objectId)
    {
        return new ASDelete
        {
            Id = activityId,
            Actor = actorUrl,
            Object = new ASTombstone { Id = objectId, Deleted = DateTime.UtcNow },
            Published = DateTime.UtcNow
        };
    }

    public ASUndo RenderUndoActivity(string activityId, string actorUrl, ASActivity activityToUndo)
    {
        return new ASUndo
        {
            Id = activityId,
            Actor = actorUrl,
            Object = activityToUndo,
            Published = DateTime.UtcNow
        };
    }

    public ASBlock RenderBlockActivity(string activityId, string actorUrl, string targetUrl)
    {
        return new ASBlock
        {
            Id = activityId,
            Actor = actorUrl,
            Object = targetUrl,
            Published = DateTime.UtcNow
        };
    }

    public ASMove RenderMoveActivity(string activityId, string actorUrl, string objectUrl, string targetUrl)
    {
        return new ASMove
        {
            Id = activityId,
            Actor = actorUrl,
            Object = objectUrl,
            Target = targetUrl,
            Published = DateTime.UtcNow
        };
    }

    public ASFlag RenderFlagActivity(string activityId, string actorUrl, string objectUrl, string? content = null)
    {
        return new ASFlag
        {
            Id = activityId,
            Actor = actorUrl,
            Object = objectUrl,
            Content = content,
            Published = DateTime.UtcNow
        };
    }

    public ASQuoteRequest RenderQuoteRequestActivity(string activityId, string actorUrl, string quoteUrl, string noteUrl)
    {
        return new ASQuoteRequest
        {
            Id = activityId,
            Actor = actorUrl,
            Object = quoteUrl,
            Target = noteUrl,
            Published = DateTime.UtcNow
        };
    }

    public ASCollection RenderFollowersCollection(string actorUri, string[] items)
    {
        return new ASCollection
        {
            Id = $"{actorUri}/followers",
            TotalItems = items.Length,
            First = $"{actorUri}/followers?page=true",
            Items = items
        };
    }

    public ASOrderedCollection RenderFollowersOrderedCollection(string actorUri, string[] items)
    {
        return new ASOrderedCollection
        {
            Id = $"{actorUri}/followers",
            TotalItems = items.Length,
            First = $"{actorUri}/followers?page=true",
            OrderedItems = items
        };
    }

    public ASCollectionPage RenderFollowersCollectionPage(string actorUri, string[] items, string? next = null, string? prev = null)
    {
        return new ASCollectionPage
        {
            Id = $"{actorUri}/followers?page=true",
            TotalItems = items.Length,
            Items = items,
            Next = next,
            Prev = prev
        };
    }

    public Dictionary<string, object> ToDictionary(ASObject obj)
    {
        return ASDeserializer.ToDictionary(obj) ?? [];
    }

    public string Serialize(ASObject obj)
    {
        return ASSerializer.Serialize(obj);
    }
}