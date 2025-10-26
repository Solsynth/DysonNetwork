using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NpgsqlTypes;
using Google.Protobuf.WellKnownTypes;

namespace DysonNetwork.Shared.Models;

public enum PostType
{
    Moment,
    Article
}

public enum PostVisibility
{
    Public,
    Friends,
    Unlisted,
    Private
}

public enum PostPinMode
{
    PublisherPage,
    RealmPage,
    ReplyPage,
}

public class SnPost : ModelBase, IIdentifiedResource, IActivity
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    [MaxLength(1024)] public string? Slug { get; set; }
    public Instant? EditedAt { get; set; }
    public Instant? PublishedAt { get; set; }
    public PostVisibility Visibility { get; set; } = PostVisibility.Public;

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? Content { get; set; }

    public PostType Type { get; set; }
    public PostPinMode? PinMode { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public List<ContentSensitiveMark>? SensitiveMarks { get; set; } = [];
    [Column(TypeName = "jsonb")] public PostEmbedView? EmbedView { get; set; }

    public int ViewsUnique { get; set; }
    public int ViewsTotal { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    public decimal AwardedScore { get; set; }
    [NotMapped] public Dictionary<string, int> ReactionsCount { get; set; } = new();
    [NotMapped] public int RepliesCount { get; set; }
    [NotMapped] public Dictionary<string, bool>? ReactionsMade { get; set; }

    public bool RepliedGone { get; set; }
    public bool ForwardedGone { get; set; }

    public Guid? RepliedPostId { get; set; }
    public SnPost? RepliedPost { get; set; }
    public Guid? ForwardedPostId { get; set; }
    public SnPost? ForwardedPost { get; set; }

    public Guid? RealmId { get; set; }
    [NotMapped] public SnRealm? Realm { get; set; }

    [Column(TypeName = "jsonb")] public List<SnCloudFileReferenceObject> Attachments { get; set; } = [];

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;

    public List<SnPostAward> Awards { get; set; } = [];
    [JsonIgnore] public List<SnPostReaction> Reactions { get; set; } = [];
    public List<SnPostTag> Tags { get; set; } = [];
    public List<SnPostCategory> Categories { get; set; } = [];
    [JsonIgnore] public List<SnPostCollection> Collections { get; set; } = [];
    public List<SnPostFeaturedRecord> FeaturedRecords { get; set; } = [];

    [JsonIgnore] public bool Empty => Content == null && Attachments.Count == 0 && ForwardedPostId == null;
    [NotMapped] public bool IsTruncated { get; set; } = false;

    public string ResourceIdentifier => $"post:{Id}";

    public Post ToProtoValue()
    {
        var proto = new Post
        {
            Id = Id.ToString(),
            Title = Title ?? string.Empty,
            Description = Description ?? string.Empty,
            Slug = Slug ?? string.Empty,
            Visibility = (Proto.PostVisibility)((int)Visibility + 1),
            Type = (Proto.PostType)((int)Type + 1),
            ViewsUnique = ViewsUnique,
            ViewsTotal = ViewsTotal,
            Upvotes = Upvotes,
            Downvotes = Downvotes,
            AwardedScore = (double)AwardedScore,
            ReactionsCount = { ReactionsCount },
            RepliesCount = RepliesCount,
            ReactionsMade = { ReactionsMade ?? new Dictionary<string, bool>() },
            RepliedGone = RepliedGone,
            ForwardedGone = ForwardedGone,
            PublisherId = PublisherId.ToString(),
            Publisher = Publisher.ToProtoValue(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };

        if (EditedAt.HasValue)
        {
            proto.EditedAt = Timestamp.FromDateTimeOffset(EditedAt.Value.ToDateTimeOffset());
        }

        if (PublishedAt.HasValue)
        {
            proto.PublishedAt = Timestamp.FromDateTimeOffset(PublishedAt.Value.ToDateTimeOffset());
        }

        if (Content != null)
        {
            proto.Content = Content;
        }

        if (PinMode.HasValue)
        {
            proto.PinMode = (Proto.PostPinMode)((int)PinMode.Value + 1);
        }

        if (Meta != null)
        {
            proto.Meta = GrpcTypeHelper.ConvertObjectToByteString(Meta);
        }

        if (SensitiveMarks != null)
        {
            proto.SensitiveMarks = GrpcTypeHelper.ConvertObjectToByteString(SensitiveMarks);
        }

        if (EmbedView != null)
        {
            proto.EmbedView = EmbedView.ToProtoValue();
        }

        if (RepliedPostId.HasValue)
        {
            proto.RepliedPostId = RepliedPostId.Value.ToString();
            if (RepliedPost != null)
            {
                proto.RepliedPost = RepliedPost.ToProtoValue();
            }
        }

        if (ForwardedPostId.HasValue)
        {
            proto.ForwardedPostId = ForwardedPostId.Value.ToString();
            if (ForwardedPost != null)
            {
                proto.ForwardedPost = ForwardedPost.ToProtoValue();
            }
        }

        if (RealmId.HasValue)
        {
            proto.RealmId = RealmId.Value.ToString();
            if (Realm != null)
            {
                proto.Realm = Realm.ToProtoValue();
            }
        }

        proto.Attachments.AddRange(Attachments.Select(a => a.ToProtoValue()));
        proto.Awards.AddRange(Awards.Select(a => a.ToProtoValue()));
        proto.Reactions.AddRange(Reactions.Select(r => r.ToProtoValue()));
        proto.Tags.AddRange(Tags.Select(t => t.ToProtoValue()));
        proto.Categories.AddRange(Categories.Select(c => c.ToProtoValue()));
        proto.FeaturedRecords.AddRange(FeaturedRecords.Select(f => f.ToProtoValue()));

        if (DeletedAt.HasValue)
            proto.DeletedAt = Timestamp.FromDateTimeOffset(DeletedAt.Value.ToDateTimeOffset());

        return proto;
    }

    public static SnPost FromProtoValue(Post proto)
    {
        var post = new SnPost
        {
            Id = Guid.Parse(proto.Id),
            Title = string.IsNullOrEmpty(proto.Title) ? null : proto.Title,
            Description = string.IsNullOrEmpty(proto.Description) ? null : proto.Description,
            Slug = string.IsNullOrEmpty(proto.Slug) ? null : proto.Slug,
            Visibility = (PostVisibility)((int)proto.Visibility - 1),
            Type = (PostType)((int)proto.Type - 1),
            ViewsUnique = proto.ViewsUnique,
            ViewsTotal = proto.ViewsTotal,
            Upvotes = proto.Upvotes,
            Downvotes = proto.Downvotes,
            AwardedScore = (decimal)proto.AwardedScore,
            ReactionsCount = proto.ReactionsCount.ToDictionary(kv => kv.Key, kv => kv.Value),
            RepliesCount = proto.RepliesCount,
            ReactionsMade = proto.ReactionsMade.ToDictionary(kv => kv.Key, kv => kv.Value),
            RepliedGone = proto.RepliedGone,
            ForwardedGone = proto.ForwardedGone,
            PublisherId = Guid.Parse(proto.PublisherId),
            Publisher = SnPublisher.FromProtoValue(proto.Publisher),
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset())
        };

        if (proto.EditedAt is not null)
            post.EditedAt = Instant.FromDateTimeOffset(proto.EditedAt.ToDateTimeOffset());

        if (proto.PublishedAt is not null)
            post.PublishedAt = Instant.FromDateTimeOffset(proto.PublishedAt.ToDateTimeOffset());

        if (!string.IsNullOrEmpty(proto.Content))
            post.Content = proto.Content;

        if (proto is { HasPinMode: true, PinMode: > 0 })
            post.PinMode = (PostPinMode)(proto.PinMode - 1);

        if (proto.Meta != null)
            post.Meta = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object>>(proto.Meta);

        if (proto.SensitiveMarks != null)
            post.SensitiveMarks =
                GrpcTypeHelper.ConvertByteStringToObject<List<ContentSensitiveMark>>(proto.SensitiveMarks);

        if (proto.EmbedView is not null)
            post.EmbedView = PostEmbedView.FromProtoValue(proto.EmbedView);

        if (!string.IsNullOrEmpty(proto.RepliedPostId))
        {
            post.RepliedPostId = Guid.Parse(proto.RepliedPostId);
            if (proto.RepliedPost is not null)
                post.RepliedPost = FromProtoValue(proto.RepliedPost);
        }

        if (!string.IsNullOrEmpty(proto.ForwardedPostId))
        {
            post.ForwardedPostId = Guid.Parse(proto.ForwardedPostId);
            if (proto.ForwardedPost is not null)
                post.ForwardedPost = FromProtoValue(proto.ForwardedPost);
        }

        if (!string.IsNullOrEmpty(proto.RealmId))
        {
            post.RealmId = Guid.Parse(proto.RealmId);
            if (proto.Realm is not null)
                post.Realm = SnRealm.FromProtoValue(proto.Realm);
        }

        post.Attachments.AddRange(proto.Attachments.Select(SnCloudFileReferenceObject.FromProtoValue));
        post.Awards.AddRange(proto.Awards.Select(a => new SnPostAward
        {
            Id = Guid.Parse(a.Id), PostId = Guid.Parse(a.PostId), AccountId = Guid.Parse(a.AccountId),
            Amount = (decimal)a.Amount, Attitude = (PostReactionAttitude)((int)a.Attitude - 1),
            Message = string.IsNullOrEmpty(a.Message) ? null : a.Message,
            CreatedAt = Instant.FromDateTimeOffset(a.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(a.UpdatedAt.ToDateTimeOffset())
        }));
        post.Reactions.AddRange(proto.Reactions.Select(SnPostReaction.FromProtoValue));
        post.Tags.AddRange(proto.Tags.Select(SnPostTag.FromProtoValue));
        post.Categories.AddRange(proto.Categories.Select(SnPostCategory.FromProtoValue));
        post.FeaturedRecords.AddRange(proto.FeaturedRecords.Select(SnPostFeaturedRecord.FromProtoValue));

        if (proto.DeletedAt is not null)
            post.DeletedAt = Instant.FromDateTimeOffset(proto.DeletedAt.ToDateTimeOffset());

        return post;
    }

    public SnActivity ToActivity()
    {
        return new SnActivity()
        {
            CreatedAt = PublishedAt ?? CreatedAt,
            UpdatedAt = UpdatedAt,
            DeletedAt = DeletedAt,
            Id = Id,
            Type = RepliedPostId is null ? "posts.new" : "posts.new.replies",
            ResourceIdentifier = ResourceIdentifier,
            Data = this
        };
    }
}

public class SnPostTag : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [JsonIgnore] public List<SnPost> Posts { get; set; } = new List<SnPost>();

    [NotMapped] public int? Usage { get; set; }

    public PostTag ToProtoValue()
    {
        return new PostTag
        {
            Id = Id.ToString(),
            Slug = Slug,
            Name = Name ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };
    }

    public static SnPostTag FromProtoValue(PostTag proto)
    {
        return new SnPostTag
        {
            Id = Guid.Parse(proto.Id),
            Slug = proto.Slug,
            Name = proto.Name != string.Empty ? proto.Name : null,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset())
        };
    }
}

public class SnPostCategory : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [JsonIgnore] public List<SnPost> Posts { get; set; } = new List<SnPost>();

    [NotMapped] public int? Usage { get; set; }

    public PostCategory ToProtoValue()
    {
        return new PostCategory
        {
            Id = Id.ToString(),
            Slug = Slug,
            Name = Name ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };
    }

    public static SnPostCategory FromProtoValue(PostCategory proto)
    {
        return new SnPostCategory
        {
            Id = Guid.Parse(proto.Id),
            Slug = proto.Slug,
            Name = proto.Name != string.Empty ? proto.Name : null,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset())
        };
    }
}

public class SnPostCategorySubscription : ModelBase
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }

    public Guid? CategoryId { get; set; }
    public SnPostCategory? Category { get; set; }
    public Guid? TagId { get; set; }
    public SnPostTag? Tag { get; set; }
}

public class SnPostCollection : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }

    public SnPublisher Publisher { get; set; } = null!;

    public List<SnPost> Posts { get; set; } = new List<SnPost>();
}

public class SnPostFeaturedRecord : ModelBase
{
    public Guid Id { get; set; }
    public Guid PostId { get; set; }
    [JsonIgnore] public SnPost Post { get; set; } = null!;
    public Instant? FeaturedAt { get; set; }
    public int SocialCredits { get; set; }

    public PostFeaturedRecord ToProtoValue()
    {
        var proto = new PostFeaturedRecord
        {
            Id = Id.ToString(),
            PostId = PostId.ToString(),
            SocialCredits = SocialCredits,
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };
        if (FeaturedAt.HasValue)
        {
            proto.FeaturedAt = Timestamp.FromDateTimeOffset(FeaturedAt.Value.ToDateTimeOffset());
        }

        return proto;
    }

    public static SnPostFeaturedRecord FromProtoValue(PostFeaturedRecord proto)
    {
        return new SnPostFeaturedRecord
        {
            Id = Guid.Parse(proto.Id),
            PostId = Guid.Parse(proto.PostId),
            SocialCredits = proto.SocialCredits,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset()),
            FeaturedAt = proto.FeaturedAt != null ? Instant.FromDateTimeOffset(proto.FeaturedAt.ToDateTimeOffset()) : null
        };
    }
}

public enum PostReactionAttitude
{
    Positive,
    Neutral,
    Negative,
}

public class SnPostReaction : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(256)] public string Symbol { get; set; } = null!;
    public PostReactionAttitude Attitude { get; set; }

    public Guid PostId { get; set; }
    [JsonIgnore] public SnPost Post { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }

    public PostReaction ToProtoValue()
    {
        var proto = new PostReaction
        {
            Id = Id.ToString(),
            Symbol = Symbol,
            Attitude = (Proto.PostReactionAttitude)((int)Attitude + 1),
            PostId = PostId.ToString(),
            AccountId = AccountId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };
        if (Account != null)
        {
            proto.Account = Account.ToProtoValue();
        }

        return proto;
    }

    public static SnPostReaction FromProtoValue(Proto.PostReaction proto)
    {
        return new SnPostReaction
        {
            Id = Guid.Parse(proto.Id),
            Symbol = proto.Symbol,
            Attitude = (PostReactionAttitude)((int)proto.Attitude - 1),
            PostId = Guid.Parse(proto.PostId),
            AccountId = Guid.Parse(proto.AccountId),
            Account = proto.Account != null ? SnAccount.FromProtoValue(proto.Account) : null,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset())
        };
    }
}

public class SnPostAward : ModelBase
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public PostReactionAttitude Attitude { get; set; }
    [MaxLength(4096)] public string? Message { get; set; }

    public Guid PostId { get; set; }
    [JsonIgnore] public SnPost Post { get; set; } = null!;
    public Guid AccountId { get; set; }

    public PostAward ToProtoValue()
    {
        var proto = new PostAward
        {
            Id = Id.ToString(),
            Amount = (double)Amount,
            Attitude = (Proto.PostReactionAttitude)((int)Attitude + 1),
            PostId = PostId.ToString(),
            AccountId = AccountId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };
        if (Message != null)
            proto.Message = Message;
        return proto;
    }
}

/// <summary>
/// This model is used to tell the client to render a WebView / iframe
/// Usually external website and web pages
/// Used as a JSON column
/// </summary>
public class PostEmbedView
{
    public string Uri { get; set; } = null!;
    public double? AspectRatio { get; set; }
    public PostEmbedViewRenderer Renderer { get; set; } = PostEmbedViewRenderer.WebView;

    public Proto.PostEmbedView ToProtoValue()
    {
        var proto = new Proto.PostEmbedView
        {
            Uri = Uri,
            Renderer = (Proto.PostEmbedViewRenderer)(int)Renderer
        };
        if (AspectRatio.HasValue)
        {
            proto.AspectRatio = AspectRatio.Value;
        }

        return proto;
    }

    public static PostEmbedView FromProtoValue(Proto.PostEmbedView proto)
    {
        return new PostEmbedView
        {
            Uri = proto.Uri,
            AspectRatio = proto.HasAspectRatio ? proto.AspectRatio : null,
            Renderer = (PostEmbedViewRenderer)((int)proto.Renderer - 1)
        };
    }
}

public enum PostEmbedViewRenderer
{
    WebView
}
