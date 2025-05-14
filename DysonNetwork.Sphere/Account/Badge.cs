using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class Badge : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(1024)] public string? Label { get; set; }
    [MaxLength(4096)] public string? Caption { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();
    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;
}