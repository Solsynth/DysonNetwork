using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public enum StatusAttitude
{
    Positive,
    Negative,
    Neutral
}

public class Status : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StatusAttitude Attitude { get; set; }
    [NotMapped] public bool IsOnline { get; set; }
    public bool IsInvisible { get; set; }
    public bool IsNotDisturb { get; set; }
    [MaxLength(1024)] public string? Label { get; set; }
    public Instant? ClearedAt { get; set; }
    
    public long AccountId { get; set; }
    public Account Account { get; set; } = null!;
}