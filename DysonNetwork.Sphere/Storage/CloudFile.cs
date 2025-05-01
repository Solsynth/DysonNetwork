using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Sphere.Storage;

public abstract class RemoteStorageConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Bucket { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string SecretId { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool EnableSigned { get; set; }
    public bool EnableSsl { get; set; }
    public string? ImageProxy { get; set; }
    public string? AccessProxy { get; set; }
}

public class CloudFile : ModelBase
{
    [MaxLength(128)] public string Id { get; set; } = Guid.NewGuid().ToString();
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Description { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? FileMeta { get; set; } = null!;
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? UserMeta { get; set; } = null!;
    [Column(TypeName = "jsonb")] List<CloudFileSensitiveMark> SensitiveMarks { get; set; } = new();
    [MaxLength(256)] public string? MimeType { get; set; }
    [MaxLength(256)] public string? Hash { get; set; }
    public long Size { get; set; }
    public Instant? UploadedAt { get; set; }
    public Instant? ExpiredAt { get; set; }
    [MaxLength(128)] public string? UploadedTo { get; set; }
    public bool HasCompression { get; set; }= false;

    // Metrics
    // When this used count keep zero, it means it's not used by anybody, so it can be recycled
    public int UsedCount { get; set; } = 0;

    [JsonIgnore] public Account.Account Account { get; set; } = null!;
}

public enum CloudFileSensitiveMark
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