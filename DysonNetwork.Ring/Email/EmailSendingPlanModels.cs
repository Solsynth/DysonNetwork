using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring.Email;

public enum EmailSendingPlanStatus
{
    Scheduled = 0,
    Paused = 1,
    Completed = 2
}

public enum EmailSendingPlanRecipientStatus
{
    Pending = 0,
    Sent = 1,
    Skipped = 2,
    Failed = 3
}

[Index(nameof(SendingPlanKey), nameof(DeletedAt), IsUnique = true)]
public class SnEmailSendingPlan : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string? SendingPlanKey { get; set; }
    public Guid CreatedByAccountId { get; set; }
    [MaxLength(1024)] public string Subject { get; set; } = string.Empty;
    [MaxLength(1_000_000)] public string HtmlBody { get; set; } = string.Empty;
    public bool BroadcastToAll { get; set; }
    public int RecipientCount { get; set; }
    public int MaxEmailsPerInterval { get; set; }
    public int IntervalMinutes { get; set; }
    public int? MaxEmailsPerDay { get; set; }
    public EmailSendingPlanStatus Status { get; set; } = EmailSendingPlanStatus.Scheduled;
    public int AdvancedIntervalsCount { get; set; }
    public Instant PlannedStartAt { get; set; }
    public Instant? NextIntervalAt { get; set; }
    public Instant? LastAdvancedAt { get; set; }
    public Instant? PausedAt { get; set; }
    public Instant? CompletedAt { get; set; }
}

[Index(nameof(PlanId), nameof(AccountId), nameof(DeletedAt), IsUnique = true)]
public class SnEmailSendingPlanRecipient : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public Guid AccountId { get; set; }
    [MaxLength(1024)] public string? RecipientNameSnapshot { get; set; }
    public EmailSendingPlanRecipientStatus Status { get; set; } = EmailSendingPlanRecipientStatus.Pending;
    public int AttemptCount { get; set; }
    public int? LastIntervalNumber { get; set; }
    [MaxLength(1024)] public string? LastResolvedEmail { get; set; }
    [MaxLength(4096)] public string? LastError { get; set; }
    public Instant? ProcessedAt { get; set; }
}

[Index(nameof(PlanId), nameof(IntervalNumber), nameof(DeletedAt), IsUnique = true)]
public class SnEmailSendingPlanAdvance : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public int IntervalNumber { get; set; }
    public bool IsManual { get; set; }
    public int AttemptedCount { get; set; }
    public int SentCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int PendingCountAfter { get; set; }
    public Instant StartedAt { get; set; }
    public Instant CompletedAt { get; set; }
}
