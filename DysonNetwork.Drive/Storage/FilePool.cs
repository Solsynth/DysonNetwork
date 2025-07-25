using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Drive.Storage;

public class RemoteStorageConfig
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
    public Duration? Expiration { get; set; }
}

public class BillingConfig
{
    public double CostMultiplier { get; set; } = 1.0;
}

public class FilePool : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [Column(TypeName = "jsonb")] public RemoteStorageConfig StorageConfig { get; set; } = new();
    [Column(TypeName = "jsonb")] public BillingConfig BillingConfig { get; set; } = new();
    
    public string ResourceIdentifier => $"file-pool/{Id}";
}