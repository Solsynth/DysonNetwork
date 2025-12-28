using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Uri), IsUnique = true)]
public class SnFediverseContent : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [MaxLength(2048)]
    public string Uri { get; set; } = null!;
    
    public FediverseContentType Type { get; set; }
    
    [MaxLength(1024)]
    public string? Title { get; set; }
    
    [MaxLength(4096)]
    public string? Summary { get; set; }
    
    public string? Content { get; set; }
    
    public string? ContentHtml { get; set; }
    
    [MaxLength(2048)]
    public string? Language { get; set; }
    
    [MaxLength(2048)]
    public string? InReplyTo { get; set; }
    
    [MaxLength(2048)]
    public string? AnnouncedContentUri { get; set; }
    
    public Instant? PublishedAt { get; set; }
    public Instant? EditedAt { get; set; }
    
    public bool IsSensitive { get; set; } = false;
    
    [Column(TypeName = "jsonb")]
    public List<ContentAttachment>? Attachments { get; set; }
    
    [Column(TypeName = "jsonb")]
    public List<ContentMention>? Mentions { get; set; }
    
    [Column(TypeName = "jsonb")]
    public List<ContentTag>? Tags { get; set; }
    
    [Column(TypeName = "jsonb")]
    public List<ContentEmoji>? Emojis { get; set; }
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object>? Metadata { get; set; }
    
    public Guid ActorId { get; set; }
    [JsonIgnore]
    public SnFediverseActor Actor { get; set; } = null!;
    
    public Guid InstanceId { get; set; }
    [JsonIgnore]
    public SnFediverseInstance Instance { get; set; } = null!;
    
    [JsonIgnore]
    public ICollection<SnFediverseActivity> Activities { get; set; } = [];
    
    [JsonIgnore]
    public ICollection<SnFediverseReaction> Reactions { get; set; } = [];
    
    public int ReplyCount { get; set; }
    public int BoostCount { get; set; }
    public int LikeCount { get; set; }
    
    public Guid? LocalPostId { get; set; }
    [NotMapped]
    public SnPost? LocalPost { get; set; }
}

public enum FediverseContentType
{
    Note,
    Article,
    Image,
    Video,
    Audio,
    Page,
    Question,
    Event,
    Document
}

public class ContentAttachment
{
    [MaxLength(2048)]
    public string? Url { get; set; }
    
    [MaxLength(2048)]
    public string? MediaType { get; set; }
    
    [MaxLength(1024)]
    public string? Name { get; set; }
    
    public int? Width { get; set; }
    public int? Height { get; set; }
    
    [MaxLength(64)]
    public string? Blurhash { get; set; }
}

public class ContentMention
{
    [MaxLength(256)]
    public string? Username { get; set; }
    
    [MaxLength(2048)]
    public string? Url { get; set; }
    
    [MaxLength(2048)]
    public string? ActorUri { get; set; }
}

public class ContentTag
{
    [MaxLength(256)]
    public string? Name { get; set; }
    
    [MaxLength(2048)]
    public string? Url { get; set; }
}

public class ContentEmoji
{
    [MaxLength(64)]
    public string? Shortcode { get; set; }
    
    [MaxLength(2048)]
    public string? StaticUrl { get; set; }
    
    [MaxLength(2048)]
    public string? Url { get; set; }
}
