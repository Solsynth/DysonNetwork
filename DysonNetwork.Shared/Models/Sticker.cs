using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Slug))] // The slug index shouldn't be unique, the sticker slug can be repeated across packs.
public class SnSticker : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Slug { get; set; } = null!;

    // Outdated fields, for backward compability
    [MaxLength(32)] public string? ImageId { get; set; }

    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Image { get; set; } = null!;

    public Guid PackId { get; set; }
    [JsonIgnore] public StickerPack Pack { get; set; } = null!;

    public string ResourceIdentifier => $"sticker/{Id}";
}

[Index(nameof(Prefix), IsUnique = true)]
public class StickerPack : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Name { get; set; } = null!;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    [MaxLength(128)] public string Prefix { get; set; } = null!;

    public List<SnSticker> Stickers { get; set; } = [];
    [JsonIgnore] public List<StickerPackOwnership> Ownerships { get; set; } = [];

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;
}

public class StickerPackOwnership : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PackId { get; set; }
    public StickerPack Pack { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;
}
