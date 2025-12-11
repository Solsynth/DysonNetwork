using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Google.Api;

namespace DysonNetwork.Shared.Models;

public enum PublicationSiteMode
{
    /// <summary>
    /// The fully managed mode which allow other server to handle all the requests
    /// Including the rendering, data fetching etc. /// Very easy to use and efficient.
    /// </summary>
    FullyManaged,

    /// <summary>
    /// The self-managed mode allows user to upload their own site assets and use the Solar Network Pages
    /// as a static site hosting platform.
    /// </summary>
    SelfManaged
}

public class PublicationSiteConfig
{
    public string? StyleOverride { get; set; }
    public List<PublicationSiteNavItem>? NavItems { get; set; } = [];
}

public class PublicationSiteNavItem
{
    [MaxLength(1024)] public string Label { get; set; } = null!;
    [MaxLength(8192)] public string Href { get; set; } = null!;
}

public class SnPublicationSite : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string Slug { get; set; } = null!;
    [MaxLength(4096)] public string Name { get; set; } = null!;
    [MaxLength(8192)] public string? Description { get; set; }

    public PublicationSiteMode Mode { get; set; } = PublicationSiteMode.FullyManaged;
    public List<SnPublicationPage> Pages { get; set; } = [];
    [Column(TypeName = "jsonb")] public PublicationSiteConfig Config { get; set; } = new();

    public Guid PublisherId { get; set; }
    [NotMapped] public SnPublisher Publisher { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }
}

public enum PublicationPageType
{
    /// <summary>
    /// The HTML page type allows user adding a custom HTML page in the fully managed mode.
    /// </summary>
    HtmlPage,
    /// <summary>
    /// The redirect mode allows user to create a shortcut for their own link.
    /// such as example.solian.page/rickroll -- DyZ 301 -> youtube.com/...
    /// </summary>
    Redirect
}

public class SnPublicationPage : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public PublicationPageType Type { get; set; } = PublicationPageType.HtmlPage;
    [MaxLength(8192)] public string Path { get; set; } = "/";
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Config { get; set; } = new();

    public Guid SiteId { get; set; }
    [JsonIgnore] public SnPublicationSite Site { get; set; } = null!;
}