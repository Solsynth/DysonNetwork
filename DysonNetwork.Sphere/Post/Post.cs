using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;
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

public class Post : ModelBase
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

    public int ViewsUnique { get; set; }
    public int ViewsTotal { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }
    [NotMapped] public Dictionary<string, int> ReactionsCount { get; set; } = new();

    public Guid? ThreadedPostId { get; set; }
    public Post? ThreadedPost { get; set; }
    public Guid? RepliedPostId { get; set; }
    public Post? RepliedPost { get; set; }
    public Guid? ForwardedPostId { get; set; }
    public Post? ForwardedPost { get; set; }
    public ICollection<CloudFile> Attachments { get; set; } = new List<CloudFile>();

    [JsonIgnore] public NpgsqlTsVector SearchVector { get; set; } = null!;

    public Publisher.Publisher Publisher { get; set; } = null!;
    public ICollection<PostReaction> Reactions { get; set; } = new List<PostReaction>();
    public ICollection<PostTag> Tags { get; set; } = new List<PostTag>();
    public ICollection<PostCategory> Categories { get; set; } = new List<PostCategory>();
    public ICollection<PostCollection> Collections { get; set; } = new List<PostCollection>();

    [JsonIgnore] public bool Empty => Content == null && Attachments.Count == 0 && ForwardedPostId == null;
    [NotMapped] public bool IsTruncated = false;
}

public class PostTag : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    public ICollection<Post> Posts { get; set; } = new List<Post>();
}

public class PostCategory : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(256)] public string? Name { get; set; }
    public ICollection<Post> Posts { get; set; } = new List<Post>();
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
    public Account.Account Account { get; set; } = null!;
}