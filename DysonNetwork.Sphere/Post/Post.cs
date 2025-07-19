using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
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

public class Post : ModelBase, IIdentifiedResource, IActivity
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    [MaxLength(128)] public string? Language { get; set; }
    public Instant? EditedAt { get; set; }
    public Instant? PublishedAt { get; set; }
    public PostVisibility Visibility { get; set; } = PostVisibility.Public;

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? Content { get; set; }

    public PostType Type { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public List<ContentSensitiveMark>? SensitiveMarks { get; set; } = [];

    public int ViewsUnique { get; set; }
    public int ViewsTotal { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    [NotMapped] public Dictionary<string, int> ReactionsCount { get; set; } = new();
    [NotMapped] public int RepliesCount { get; set; }

    public Guid? RepliedPostId { get; set; }
    public Post? RepliedPost { get; set; }
    public Guid? ForwardedPostId { get; set; }
    public Post? ForwardedPost { get; set; }

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
}

public class PostCategory : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    [JsonIgnore] public ICollection<Post> Posts { get; set; } = new List<Post>();
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
