using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.Models;

public class SnFileObject : ModelBase
{
    [MaxLength(32)] public string Id { get; set; }
    public Guid AccountId { get; set; }

    public long Size { get; set; }

    [Column(TypeName = "jsonb")] public Dictionary<string, object?>? Meta { get; set; }
    [MaxLength(256)] public string? MimeType { get; set; }
    [MaxLength(256)] public string? Hash { get; set; }
    public bool HasCompression { get; set; } = false;
    public bool HasThumbnail { get; set; } = false;

    public List<SnFileReplica> FileReplicas { get; set; } = [];
}

public enum SnFileReplicaStatus
{
    Pending,
    Uploading,
    Available,
    Failed,
    Deprecated
}

public class SnFileReplica : ModelBase
{
    public Guid Id { get; set; }

    [MaxLength(32)] public string ObjectId { get; set; }
    [JsonIgnore] public SnFileObject Object { get; set; } = null!;

    public Guid? PoolId { get; set; }
    public FilePool? Pool { get; set; } = null!;

    [MaxLength(128)]
    public string StorageId { get; set; } = null!;

    public SnFileReplicaStatus Status { get; set; } = SnFileReplicaStatus.Pending;

    public bool IsPrimary { get; set; } = false;
}

public enum SnFilePermissionType
{
    Someone,
    Anyone
}

public enum SnFilePermissionLevel
{
    Read,
    Write
}

public class SnFilePermission : ModelBase
{
    public Guid Id { get; set; }
    public string FileId { get; set; } = null!;
    public SnFilePermissionType SubjectType { get; set; } = SnFilePermissionType.Someone;
    public string SubjectId { get; set; } = null!;
    public SnFilePermissionLevel Permission { get; set; } = SnFilePermissionLevel.Read;
}