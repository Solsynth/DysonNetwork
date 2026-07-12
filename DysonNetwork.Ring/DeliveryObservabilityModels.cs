using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Ring;

public enum DeliveryOutcome
{
    Success = 0,
    Failure = 1,
    InvalidToken = 2,
    Skipped = 3
}

public class SnEmailDeliveryRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(64)] public string Source { get; set; } = string.Empty;
    [MaxLength(64)] public string Provider { get; set; } = string.Empty;
    public DeliveryOutcome Outcome { get; set; }
    public long DurationMilliseconds { get; set; }
    [MaxLength(4096)] public string? Error { get; set; }
}

public class SnNotificationDeliveryRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Topic { get; set; } = string.Empty;
    [MaxLength(1024)] public string? AppId { get; set; }
    [MaxLength(64)] public string? PushType { get; set; }
    [MaxLength(64)] public string Provider { get; set; } = string.Empty;
    public DeliveryOutcome Outcome { get; set; }
    public long DurationMilliseconds { get; set; }
    [MaxLength(4096)] public string? Error { get; set; }
}

public class SnNotificationSendRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Topic { get; set; } = string.Empty;
    [MaxLength(1024)] public string? AppId { get; set; }
    [MaxLength(64)] public string? PushType { get; set; }
    [MaxLength(64)] public string Source { get; set; } = string.Empty;
}
