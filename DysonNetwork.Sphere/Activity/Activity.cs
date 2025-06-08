using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NodaTime;

namespace DysonNetwork.Sphere.Activity;

public interface IActivity
{
    public Activity ToActivity();
}

[NotMapped]
public class Activity : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(4096)] public string ResourceIdentifier { get; set; } = null!;
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();

    public object? Data { get; set; }

    // Outdated fields, for backward compability
    public int Visibility => 0;

    public static Activity Empty()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new Activity
        {
            CreatedAt = now,
            UpdatedAt = now,
            Id = Guid.NewGuid(),
            Type = "empty",
            ResourceIdentifier = "none"
        };
    }
}