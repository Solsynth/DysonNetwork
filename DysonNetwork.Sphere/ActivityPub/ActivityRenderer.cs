using DysonNetwork.Shared.ActivityStreams;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityRenderer(IConfiguration configuration)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string BaseUrl => $"https://{Domain}";
    private string FileBaseUrl => configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

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