using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Sphere.Activity;
using NodaTime;
using NpgsqlTypes;

namespace DysonNetwork.Sphere.Post;

public enum PostType
{
    Moment,
    Article,
    Video
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

public class Post : ModelBase, IIdentifiedResource, IActivity
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

    public int ViewsUnique { get; set; }
    public int ViewsTotal { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    [NotMapped] public Dictionary<string, int> ReactionsCount { get; set; } = new();
    [NotMapped] public int RepliesCount { get; set; }
    [NotMapped] public Dictionary<string, bool>? ReactionsMade { get; set; }

    public bool RepliedGone { get; set; }
    public bool ForwardedGone { get; set; }

    public Guid? RepliedPostId { get; set; }
    public Post? RepliedPost { get; set; }
    public Guid? ForwardedPostId { get; set; }
    public Post? ForwardedPost { get; set; }

    public Guid? RealmId { get; set; }
    public Realm.Realm? Realm { get; set; }

    [Column(TypeName = "jsonb")] public List<CloudFileReferenceObject> Attachments { get; set; } = [];

    [JsonIgnore] public NpgsqlTsVector SearchVector { get; set; } = null!;

    public Guid PublisherId { get; set; }
    public Publisher.Publisher Publisher { get; set; } = null!;

    public ICollection<PostReaction> Reactions { get; set; } = new List<PostReaction>();
    public ICollection<PostTag> Tags { get; set; } = new List<PostTag>();
    public ICollection<PostCategory> Categories { get; set; } = new List<PostCategory>();
    [JsonIgnore] public ICollection<PostCollection> Collections { get; set; } = new List<PostCollection>();

    [JsonIgnore] public bool Empty => Content == null && Attachments.Count == 0 && ForwardedPostId == null;
    [NotMapped] public bool IsTruncated { get; set; } = false;

    public string ResourceIdentifier => $"post:{Id}";

    public Activity.Activity ToActivity()
    {
        return new Activity.Activity()
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

public class PostTag : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [JsonIgnore] public ICollection<Post> Posts { get; set; } = new List<Post>();

    [NotMapped] public int? Usage { get; set; }
}

public class PostCategory : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [JsonIgnore] public ICollection<Post> Posts { get; set; } = new List<Post>();

    [NotMapped] public int? Usage { get; set; }
}

public class PostCategorySubscription : ModelBase
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
 
    public Guid? CategoryId { get; set; }
    public PostCategory? Category { get; set; }
    public Guid? TagId { get; set; }
    public PostTag? Tag { get; set; }
}

public class PostCollection : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }

    public Publisher.Publisher Publisher { get; set; } = null!;

    public ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class PostFeaturedRecord : ModelBase
{
    public Guid Id { get; set; }

    public Guid PostId { get; set; }
    public Post Post { get; set; } = null!;
    public Instant? FeaturedAt { get; set; }
    public int SocialCredits { get; set; }
}

public enum PostReactionAttitude
{
    Positive,
    Neutral,
    Negative,
}

public class PostReaction : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(256)] public string Symbol { get; set; } = null!;
    public PostReactionAttitude Attitude { get; set; }

    public Guid PostId { get; set; }
    [JsonIgnore] public Post Post { get; set; } = null!;
    public Guid AccountId { get; set; }
}
