using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;

namespace DysonNetwork.Sphere.Post;

public enum PostType
{
    Moment,
    Article,
    Video
}

public class Post : ModelBase
{
    public long Id { get; set; }
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? Content { get; set; }
    
    public PostType Type { get; set; }
    [Column(TypeName = "jsonb")] Dictionary<string, object>? Meta { get; set; }
    
    public int ViewsUnique { get; set; }
    public int ViewsTotal { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }

    public Post? RepliedPost { get; set; }
    public Post? ForwardedPost { get; set; }
    public ICollection<CloudFile> Attachments { get; set; } = new List<CloudFile>();

    public ICollection<PostReaction> Reactions { get; set; } = new List<PostReaction>();
    public Publisher Publisher { get; set; } = null!;
}

public enum PostReactionAttitude
{
    Positive,
    Neutral,
    Negative,
}

public class PostReaction : ModelBase
{
    public long Id { get; set; }
    [MaxLength(256)] public string Symbol { get; set; } = null!;
    public PostReactionAttitude Attitude { get; set; }

    public long PostId { get; set; }
    [JsonIgnore] public Post Post { get; set; } = null!;
    public Account.Account Account { get; set; } = null!;
}