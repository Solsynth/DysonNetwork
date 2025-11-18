using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.Models;

public class SnPublicationSite : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string Slug { get; set; } = null!;
    [MaxLength(4096)] public string Name { get; set; } = null!;
    [MaxLength(8192)] public string? Description { get; set; }

    public List<SnPublicationPage> Pages { get; set; } = [];
    
    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;
    public Guid AccountId { get; set; }
    // Preloaded via the remote services
    [NotMapped] public SnAccount? Account { get; set; }
}

public abstract class PublicationPagePresets
{
    // Will told the Isolated Island to render according to prebuilt pages by us
    public const string Landing = "landing"; // Some kind of the mixed version of the profile and the posts
    public const string Profile = "profile";
    public const string Posts = "posts";
    
    // Will told the Isolated Island to render according to the blocks to use custom stuff
    public const string Custom = "custom";
}

public class SnPublicationPage : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(8192)] public string Preset { get; set; } = PublicationPagePresets.Landing;
    [MaxLength(8192)] public string Path { get; set; } = "/";
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Config { get; set; } = new();
    
    public Guid SiteId { get; set; }
    [JsonIgnore] public SnPublicationSite Site { get; set; } = null!;
}