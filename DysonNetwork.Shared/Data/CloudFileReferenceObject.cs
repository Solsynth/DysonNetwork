using System.Text.Json;
using DysonNetwork.Shared.Proto;
using Google.Protobuf;

namespace DysonNetwork.Shared.Data;

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
/// </summary>
public class CloudFileReferenceObject : ModelBase, ICloudFile
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object?> FileMeta { get; set; } = null!;
    public Dictionary<string, object?> UserMeta { get; set; } = null!;
    public string? MimeType { get; set; }
    public string? Hash { get; set; }
    public long Size { get; set; }
    public bool HasCompression { get; set; } = false;

    public static CloudFileReferenceObject FromProtoValue(Proto.CloudFile proto)
    {
        return new CloudFileReferenceObject
        {
            Id = proto.Id,
            Name = proto.Name,
            FileMeta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                proto.FileMeta.ToStringUtf8(),
                GrpcTypeHelper.SerializerOptions
            )?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ValueKind == JsonValueKind.Undefined ? null : kvp.Value.Deserialize<object?>(GrpcTypeHelper.SerializerOptions)
            ) ?? new Dictionary<string, object?>(),
            UserMeta = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                proto.UserMeta.ToStringUtf8(),
                GrpcTypeHelper.SerializerOptions
            )?.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ValueKind == JsonValueKind.Undefined ? null : kvp.Value.Deserialize<object?>(GrpcTypeHelper.SerializerOptions)
            ) ?? new Dictionary<string, object?>(),
            MimeType = proto.MimeType,
            Hash = proto.Hash,
            Size = proto.Size,
            HasCompression = proto.HasCompression
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
            Url = string.Empty // This should be set by the caller if needed
        };

        // Convert file metadata
        proto.FileMeta = ByteString.CopyFromUtf8(
            JsonSerializer.Serialize(FileMeta, GrpcTypeHelper.SerializerOptions)
        );

        // Convert user metadata
        proto.UserMeta = ByteString.CopyFromUtf8(
            JsonSerializer.Serialize(UserMeta, GrpcTypeHelper.SerializerOptions)
        );

        return proto;
    }
}