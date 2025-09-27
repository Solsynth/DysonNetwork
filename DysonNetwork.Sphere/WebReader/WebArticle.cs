using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Sphere.WebReader;

public class WebArticle : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(4096)] public string Title { get; set; } = null!;
    [MaxLength(8192)] public string Url { get; set; } = null!;
    [MaxLength(4096)] public string? Author { get; set; }
    
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public LinkEmbed? Preview { get; set; }

    // ReSharper disable once EntityFramework.ModelValidation.UnlimitedStringLength
    public string? Content { get; set; }

    public DateTime? PublishedAt { get; set; }

    public Guid FeedId { get; set; }
    public WebFeed Feed { get; set; } = null!;
}

public class WebFeedConfig
{
    public bool ScrapPage { get; set; }
}

public class WebFeed : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(8192)] public string Url { get; set; } = null!;
    [MaxLength(4096)] public string Title { get; set; } = null!;
    [MaxLength(8192)] public string? Description { get; set; }
    
    [Column(TypeName = "jsonb")] public LinkEmbed? Preview { get; set; }
    [Column(TypeName = "jsonb")] public WebFeedConfig Config { get; set; } = new();

    public Guid PublisherId { get; set; }
    public Shared.Models.SnPublisher Publisher { get; set; } = null!;

    [JsonIgnore] public ICollection<WebArticle> Articles { get; set; } = new List<WebArticle>();
}

public class WebFeedSubscription : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public Guid FeedId { get; set; }
    public WebFeed Feed { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;
}