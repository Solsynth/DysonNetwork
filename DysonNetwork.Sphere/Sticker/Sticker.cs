using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Storage;

namespace DysonNetwork.Sphere.Sticker;

public class Sticker : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Slug { get; set; } = null!;

    public string ImageId { get; set; } = null!;
    public CloudFile Image { get; set; } = null!;
    
    public Guid PackId { get; set; }
    public StickerPack Pack { get; set; } = null!;
}

public class StickerPack : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Name { get; set; } = null!;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    [MaxLength(128)] public string Prefix { get; set; } = null!;
    
    public Guid PublisherId { get; set; }
    public Publisher.Publisher Publisher { get; set; } = null!;
}

