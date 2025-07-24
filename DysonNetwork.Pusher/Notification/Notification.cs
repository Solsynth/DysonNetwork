using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using NodaTime;

namespace DysonNetwork.Pusher.Notification;

public class Notification : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Topic { get; set; } = null!;
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(2048)] public string? Subtitle { get; set; }
    [MaxLength(4096)] public string? Content { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Meta { get; set; } = new();
    public int Priority { get; set; } = 10;
    public Instant? ViewedAt { get; set; }

    public Guid AccountId { get; set; }
}

