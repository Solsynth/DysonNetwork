using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pusher.Notification;

public enum PushProvider
{
    Apple,
    Google
}

[Index(nameof(AccountId), nameof(DeviceId), nameof(DeletedAt), IsUnique = true)]
public class PushSubscription : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [MaxLength(8192)] public string DeviceId { get; set; } = null!;
    [MaxLength(8192)] public string DeviceToken { get; set; } = null!;
    public PushProvider Provider { get; set; }
    
    public int CountDelivered { get; set; }
    public Instant? LastUsedAt { get; set; }
}