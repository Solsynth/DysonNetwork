using Google.Protobuf.WellKnownTypes;

namespace DysonNetwork.Shared.Data;

/// <summary>
/// The class that used in jsonb columns which referenced the cloud file.
/// The aim of this class is to store some properties that won't change to a file to reduce the database load.
/// </summary>
public class CloudFileReferenceObject : ModelBase, ICloudFile
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object>? FileMeta { get; set; } = null!;
    public Dictionary<string, object>? UserMeta { get; set; } = null!;
    public string? MimeType { get; set; }
    public string? Hash { get; set; }
    public long Size { get; set; }
    public bool HasCompression { get; set; } = false;

    /// <summary>
    /// Converts the current object to its protobuf representation
    /// </summary>
    public Proto.CloudFile ToProtoValue()
    {
        var proto = new Proto.CloudFile
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
        if (FileMeta != null)
        {
            foreach (var (key, value) in FileMeta)
            {
                proto.FileMeta[key] = Value.ForString(value?.ToString() ?? string.Empty);
            }
        }

        // Convert user metadata
        if (UserMeta != null)
        {
            foreach (var (key, value) in UserMeta)
            {
                proto.UserMeta[key] = Value.ForString(value?.ToString() ?? string.Empty);
            }
        }

        return proto;
    }
}