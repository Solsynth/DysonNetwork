using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnNotification : ModelBase
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

public enum PushProvider
{
    Apple,
    Google
}

[Index(nameof(AccountId), nameof(DeviceId), nameof(DeletedAt), IsUnique = true)]
public class SnNotificationPushSubscription : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [MaxLength(8192)] public string DeviceId { get; set; } = null!;
    [MaxLength(8192)] public string DeviceToken { get; set; } = null!;
    public PushProvider Provider { get; set; }
    
    public int CountDelivered { get; set; }
    public Instant? LastUsedAt { get; set; }
}