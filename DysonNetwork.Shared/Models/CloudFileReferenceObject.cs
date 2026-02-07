using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Shared.Data;
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
        var fileMeta = proto.Object != null
            ? ConvertObjectToDictionary(proto.Object.Meta)
            : ConvertToDictionary(proto.FileMeta);

        return new SnCloudFileReferenceObject
        {
            Id = proto.Id,
            Name = proto.Name,
            FileMeta = fileMeta,
            UserMeta = ConvertToDictionary(proto.UserMeta),
            SensitiveMarks = proto.HasSensitiveMarks
                ? InfraObjectCoder.ConvertByteStringToObject<List<ContentSensitiveMark>>(proto.SensitiveMarks)
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

    private static Dictionary<string, object?> ConvertObjectToDictionary(Google.Protobuf.ByteString byteString)
    {
        if (byteString.IsEmpty)
            return [];

        var jsonElement = InfraObjectCoder.ConvertByteStringToObject<JsonElement>(byteString);
        if (jsonElement.ValueKind != JsonValueKind.Object)
            return [];

        var result = new Dictionary<string, object?>();
        foreach (var property in jsonElement.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }
        return result;
    }

    private static Dictionary<string, object?> ConvertToDictionary(Google.Protobuf.ByteString byteString)
    {
        if (byteString.IsEmpty)
            return [];

        var jsonElement = InfraObjectCoder.ConvertByteStringToObject<JsonElement>(byteString);
        if (jsonElement.ValueKind != JsonValueKind.Object)
            return [];

        var result = new Dictionary<string, object?>();
        foreach (var property in jsonElement.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }
        return result;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out long l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => ConvertToDictionaryElement(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            _ => null
        };
    }

    private static Dictionary<string, object?> ConvertToDictionaryElement(JsonElement element)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ConvertJsonElement(property.Value);
        }
        return result;
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
            FileMeta = InfraObjectCoder.ConvertObjectToByteString(FileMeta),
            UserMeta = InfraObjectCoder.ConvertObjectToByteString(UserMeta),
            SensitiveMarks = InfraObjectCoder.ConvertObjectToByteString(SensitiveMarks)
        };

        return proto;
    }
}