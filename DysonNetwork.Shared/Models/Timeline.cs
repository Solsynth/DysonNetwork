using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public interface ITimelineEvent
{
    public SnTimelineEvent ToActivity();
}

[NotMapped]
public class SnTimelineEvent : ModelBase
{
    public Guid Id { get; set; }

    [MaxLength(1024)]
    public string Type { get; set; } = null!;

    [MaxLength(4096)]
    public string ResourceIdentifier { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Meta { get; set; } = new();

    public object? Data { get; set; }

    public static SnTimelineEvent Empty()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        return new SnTimelineEvent
        {
            CreatedAt = now,
            UpdatedAt = now,
            Id = Guid.NewGuid(),
            Type = "empty",
            ResourceIdentifier = "none",
        };
    }
}

