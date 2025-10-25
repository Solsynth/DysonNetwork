using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public interface IActivity
{
    public SnActivity ToActivity();
}

[NotMapped]
public class SnActivity : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(4096)] public string ResourceIdentifier { get; set; } = null!;
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();

    public object? Data { get; set; }

    public static SnActivity Empty()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnActivity
        {
            CreatedAt = now,
            UpdatedAt = now,
            Id = Guid.NewGuid(),
            Type = "empty",
            ResourceIdentifier = "none"
        };
    }
}