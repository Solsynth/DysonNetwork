using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using NodaTime;

// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength

namespace DysonNetwork.Insight.MiChan;

[Table("scheduled_tasks")]
public class MiChanScheduledTask : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Instant ScheduledAt { get; set; }

    public Duration? RecurrenceInterval { get; set; }

    public Instant? RecurrenceEndAt { get; set; }

    [Column(TypeName = "text")] public string Prompt { get; set; } = null!;
    [Column(TypeName = "text")] public string? Context { get; set; }

    [MaxLength(1024)] public string Status { get; set; } = "pending";

    public Instant? CompletedAt { get; set; }

    [Column(TypeName = "text")] public string? ErrorMessage { get; set; }

    public int ExecutionCount { get; set; }

    public Guid AccountId { get; set; }

    public bool IsActive { get; set; } = true;

    public Instant? LastExecutedAt { get; set; }
}
