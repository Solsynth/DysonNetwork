using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnActivityPubDelivery : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(2048)] public string ActivityId { get; set; } = null!;
    [MaxLength(128)] public string ActivityType { get; set; } = null!;
    [MaxLength(2048)] public string InboxUri { get; set; } = null!;
    [MaxLength(2048)] public string ActorUri { get; set; } = null!;
    
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int RetryCount { get; set; } = 0;
    
    [MaxLength(4096)] public string? ErrorMessage { get; set; }
    
    public Instant? LastAttemptAt { get; set; }
    public Instant? NextRetryAt { get; set; }
    public Instant? SentAt { get; set; }
    
    [MaxLength(2048)] public string? ResponseStatusCode { get; set; }
}

public enum DeliveryStatus
{
    Pending,
    Processing,
    Sent,
    Failed,
    ExhaustedRetries
}
