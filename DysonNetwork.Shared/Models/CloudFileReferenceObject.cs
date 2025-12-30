using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Models;

public enum ContentSensitiveMark
{
    Language,
    SexualContent,
    Violence,
    Profanity,
    HateSpeech,
    Racism,
    AdultContent,
    DrugAbuse,
    AlcoholAbuse,
    Gambling,
    SelfHarm,
    ChildAbuse,
    Other
}

/// <summary>
/// The class that used in jsonb columns which referenced the cloud file.
/// The aim of this class is to store some properties that won't change to a file to reduce the database load.
/// Supports both local cloud files (with Id) and external/fediverse files (with Url).
/// </summary>
public class SnCloudFileReferenceObject : ModelBase, ICloudFile
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> FileMeta { get; set; } = null!;
    public Dictionary<string, object?> UserMeta { get; set; } = null!;
    public List<ContentSensitiveMark>? SensitiveMarks { get; set; }
    public string? MimeType { get; set; }
    public string? Hash { get; set; }
    public long Size { get; set; }
    public bool HasCompression { get; set; } = false;
    
    [MaxLength(2048)] public string? Url { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    [MaxLength(64)] public string? Blurhash { get; set; }

    public static SnCloudFileReferenceObject FromProtoValue(Proto.CloudFile proto)
    {
        return new SnCloudFileReferenceObject
        {
            Id = proto.Id,
            Name = proto.Name,
            FileMeta = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(proto.FileMeta) ?? [],
            UserMeta = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(proto.UserMeta) ?? [],
            SensitiveMarks = proto.HasSensitiveMarks
                ? GrpcTypeHelper.ConvertByteStringToObject<List<ContentSensitiveMark>>(proto.SensitiveMarks)
                : [],
            MimeType = proto.MimeType,
            Hash = proto.Hash,
            Size = proto.Size,
            HasCompression = proto.HasCompression,
            Url = string.IsNullOrEmpty(proto.Url) ? null : proto.Url,
            Width = proto.HasWidth ? proto.Width : null,
            Height = proto.HasHeight ? proto.Height : null,
            Blurhash = proto.HasBlurhash ? proto.Blurhash : null
        };
    }

    /// <summary>
    /// Converts the current object to its protobuf representation
    /// </summary>
    public CloudFile ToProtoValue()
    {
        var proto = new CloudFile
        {
            Id = Id,
            Name = Name,
            MimeType = MimeType ?? string.Empty,
            Hash = Hash ?? string.Empty,
            Size = Size,
            HasCompression = HasCompression,
            ContentType = MimeType ?? string.Empty,
            Url = Url ?? string.Empty,
            Width = Width ?? 0,
            Height = Height ?? 0,
            Blurhash = Blurhash ?? string.Empty,
            FileMeta = GrpcTypeHelper.ConvertObjectToByteString(FileMeta),
            UserMeta = GrpcTypeHelper.ConvertObjectToByteString(UserMeta),
            SensitiveMarks = GrpcTypeHelper.ConvertObjectToByteString(SensitiveMarks)
        };

        return proto;
    }
}