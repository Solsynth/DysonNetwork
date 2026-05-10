using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum StickerSize
{
    Auto,
    Small,
    Medium,
    Large,
}

public enum StickerMode
{
    Sticker,
    Emote,
}

[Index(nameof(Slug))] // The slug index shouldn't be unique, the sticker slug can be repeated across packs.
public class SnSticker : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(128)] public string Slug { get; set; } = null!;
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject Image { get; set; } = null!;
    public StickerSize Size { get; set; } = StickerSize.Auto;
    public StickerMode Mode { get; set; } = StickerMode.Sticker;

    public Guid PackId { get; set; }
    [IgnoreMember] [JsonIgnore] public StickerPack Pack { get; set; } = null!;

    public string ResourceIdentifier => $"sticker:{Id}";

    public DySticker ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Slug = Slug,
        Name = Name,
        ImageId = Image.Id,
        Size = Size switch
        {
            StickerSize.Small => DyStickerSize.Small,
            StickerSize.Medium => DyStickerSize.Medium,
            StickerSize.Large => DyStickerSize.Large,
            _ => DyStickerSize.Auto,
        },
        Mode = Mode switch
        {
            StickerMode.Emote => DyStickerMode.Emote,
            _ => DyStickerMode.Sticker,
        },
        PackId = PackId.ToString(),
        PackPrefix = Pack?.Prefix,
        CreatedAt = CreatedAt.ToTimestamp(),
        UpdatedAt = UpdatedAt.ToTimestamp(),
        DeletedAt = DeletedAt?.ToTimestamp(),
        ImageName = Image.Name,
        ImageMimeType = Image.MimeType,
        ImageSize = Image.Size,
        ImageUrl = Image.Url,
        ImageWidth = Image.Width ?? 0,
        ImageHeight = Image.Height ?? 0,
        ImageBlurhash = Image.Blurhash,
    };

    public static SnSticker FromProtoValue(DySticker proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Slug = proto.Slug,
        Name = proto.Name,
        Image = new SnCloudFileReferenceObject
        {
            Id = proto.ImageId,
            Name = proto.ImageName ?? string.Empty,
            FileMeta = [],
            UserMeta = [],
            MimeType = proto.ImageMimeType,
            Size = proto.HasImageSize ? proto.ImageSize : 0,
            Url = proto.ImageUrl,
            Width = proto.HasImageWidth ? proto.ImageWidth : null,
            Height = proto.HasImageHeight ? proto.ImageHeight : null,
            Blurhash = proto.ImageBlurhash,
        },
        Size = proto.Size switch
        {
            DyStickerSize.Small => StickerSize.Small,
            DyStickerSize.Medium => StickerSize.Medium,
            DyStickerSize.Large => StickerSize.Large,
            _ => StickerSize.Auto,
        },
        Mode = proto.Mode switch
        {
            DyStickerMode.Emote => StickerMode.Emote,
            _ => StickerMode.Sticker,
        },
        PackId = Guid.Parse(proto.PackId),
        Pack = proto.HasPackPrefix ? new StickerPack { Prefix = proto.PackPrefix } : null!,
        CreatedAt = proto.CreatedAt.ToInstant(),
        UpdatedAt = proto.UpdatedAt.ToInstant(),
        DeletedAt = proto.DeletedAt?.ToInstant(),
    };
}

[Index(nameof(Prefix), nameof(DeletedAt), IsUnique = true)]
public class StickerPack : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Icon { get; set; }
    [MaxLength(1024)] public string Name { get; set; } = null!;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    [MaxLength(128)] public string Prefix { get; set; } = null!;

    public List<SnSticker> Stickers { get; set; } = [];

    [IgnoreMember] [JsonIgnore] public List<StickerPackOwnership> Ownerships { get; set; } = [];

    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;
    
    public string ResourceIdentifier => $"sticker.pack:{Id}";

}

public class StickerPackOwnership : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PackId { get; set; }
    public StickerPack Pack { get; set; } = null!;
    public Guid AccountId { get; set; }

    [NotMapped] public SnAccount Account { get; set; } = null!;
}
