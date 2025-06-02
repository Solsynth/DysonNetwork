using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Account;

public class Notification : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Topic { get; set; } = null!;
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(2048)] public string? Subtitle { get; set; }
    [MaxLength(4096)] public string? Content { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    public int Priority { get; set; } = 10;
    public Instant? ViewedAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;
}

public enum NotificationPushProvider
{
    Apple,
    Google
}

[Index(nameof(DeviceToken), nameof(DeviceId), nameof(AccountId), IsUnique = true)]
public class NotificationPushSubscription : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string DeviceId { get; set; } = null!;
    [MaxLength(4096)] public string DeviceToken { get; set; } = null!;
    public NotificationPushProvider Provider { get; set; }
    public Instant? LastUsedAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;
}