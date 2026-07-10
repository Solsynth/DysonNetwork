using DysonNetwork.Shared.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Develop.Models;

public enum MiniAppStage
{
    Development,
    Staging,
    Production
}

public class MiniAppManifest
{
    [Required] public string Id { get; set; } = null!;
    [Required] public string Name { get; set; } = null!;
    public string Version { get; set; } = "1.0.0";
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string Entry { get; set; } = "main.js";
    public List<string> Permissions { get; set; } = [];
    public bool Background { get; set; }
    public string? Icon { get; set; }
    public string? Homepage { get; set; }

    public string? EntryUrl { get; set; }
}

public class SnMiniApp : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;

    [MaxLength(256)] public string PluginId { get; set; } = null!;
    [MaxLength(256)] public string Name { get; set; } = null!;
    [MaxLength(32)] public string Version { get; set; } = "1.0.0";
    [MaxLength(256)] public string? Author { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    [MaxLength(2048)] public string EntryUrl { get; set; } = null!;
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Icon { get; set; }
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Background { get; set; }
    [MaxLength(2048)] public string? Homepage { get; set; }
    [MaxLength(2048)] public string? PackageUrl { get; set; }
    [MaxLength(512)] public string? PackageStorageKey { get; set; }
    [MaxLength(128)] public string? PackageSha256 { get; set; }
    public long? PackageSize { get; set; }
    
    public MiniAppStage Stage { get; set; } = MiniAppStage.Development;

    [Column(TypeName = "jsonb")] public MiniAppManifest Manifest { get; set; } = null!;
    
    public Guid ProjectId { get; set; }
    public SnDevProject Project { get; set; } = null!;
    
    [NotMapped] public SnDeveloper? Developer { get; set; }
}
