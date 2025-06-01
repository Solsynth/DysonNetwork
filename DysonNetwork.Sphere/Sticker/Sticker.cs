using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Sticker;

[Index(nameof(Slug))] // The slug index shouldn't be unique, the sticker slug can be repeated across packs.
public class Sticker : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Slug { get; set; } = null!;

    [MaxLength(32)] public string ImageId { get; set; } = null!;
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject Image { get; set; } = null!;
    
    public Guid PackId { get; set; }
    public StickerPack Pack { get; set; } = null!;

    public string ResourceIdentifier => $"sticker/{Id}";
}

[Index(nameof(Prefix), IsUnique = true)]
public class StickerPack : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Name { get; set; } = null!;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    [MaxLength(128)] public string Prefix { get; set; } = null!;
    
    public Guid PublisherId { get; set; }
    public Publisher.Publisher Publisher { get; set; } = null!;
}

