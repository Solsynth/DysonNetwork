using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;
using NpgsqlTypes;

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

    [JsonIgnore] public NpgsqlTsVector SearchVector { get; set; } = null!;

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;

    public ICollection<SnPostAward> Awards { get; set; } = null!;
    [JsonIgnore] public ICollection<SnPostReaction> Reactions { get; set; } = new List<SnPostReaction>();
    public ICollection<SnPostTag> Tags { get; set; } = new List<SnPostTag>();
    public ICollection<SnPostCategory> Categories { get; set; } = new List<SnPostCategory>();
    [JsonIgnore] public ICollection<SnPostCollection> Collections { get; set; } = new List<SnPostCollection>();

    [JsonIgnore] public bool Empty => Content == null && Attachments.Count == 0 && ForwardedPostId == null;
    [NotMapped] public bool IsTruncated { get; set; } = false;

    public string ResourceIdentifier => $"post:{Id}";

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
    [JsonIgnore] public ICollection<SnPost> Posts { get; set; } = new List<SnPost>();

    [NotMapped] public int? Usage { get; set; }
}

public class SnPostCategory : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [JsonIgnore] public ICollection<SnPost> Posts { get; set; } = new List<SnPost>();

    [NotMapped] public int? Usage { get; set; }
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

    public ICollection<SnPost> Posts { get; set; } = new List<SnPost>();
}

public class SnPostFeaturedRecord : ModelBase
{
    public Guid Id { get; set; }

    public Guid PostId { get; set; }
    public SnPost Post { get; set; } = null!;
    public Instant? FeaturedAt { get; set; }
    public int SocialCredits { get; set; }
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
}

public enum PostEmbedViewRenderer
{
    WebView
}
