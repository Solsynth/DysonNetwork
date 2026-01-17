using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Shared.Models;

public enum MiniAppStage
{
    Development,
    Staging,
    Production
}

public class MiniAppManifest
{
    public string EntryUrl { get; set; } = null!;
}

public class SnMiniApp : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;
    
    public MiniAppStage Stage { get; set; } = MiniAppStage.Development;

    [Column(TypeName = "jsonb")] public MiniAppManifest Manifest { get; set; } = null!;
    
    public Guid ProjectId { get; set; }
    public SnDevProject Project { get; set; } = null!;
    
    [NotMapped] public SnDeveloper? Developer { get; set; }
}