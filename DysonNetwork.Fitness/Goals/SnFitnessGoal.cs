using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Fitness.Goals;

public class SnFitnessGoal : ModelBase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AccountId { get; set; }

    [Required]
    public FitnessGoalType GoalType { get; set; } = FitnessGoalType.Custom;

    [Required]
    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? TargetValue { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? CurrentValue { get; set; }

    [MaxLength(32)]
    public string? Unit { get; set; }

    [Required]
    public Instant StartDate { get; set; }

    public Instant? EndDate { get; set; }

    [Required]
    public FitnessGoalStatus Status { get; set; } = FitnessGoalStatus.Active;

    [MaxLength(4096)]
    public string? Notes { get; set; }
}
