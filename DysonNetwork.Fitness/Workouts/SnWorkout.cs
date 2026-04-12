using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Fitness;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Fitness.Workouts;

public class SnWorkout : ModelBase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string? ExternalId { get; set; }

    [Required]
    public Guid AccountId { get; set; }

    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string? Description { get; set; }

    [Required]
    public WorkoutType Type { get; set; } = WorkoutType.Other;

    [Required]
    public Instant StartTime { get; set; }

    public Instant? EndTime { get; set; }

    public Duration? Duration { get; set; }

    public int? CaloriesBurned { get; set; }

    [Column(TypeName = "jsonb")]
    public JsonDocument? Meta { get; set; }

    public decimal? Distance { get; set; }
    
    [MaxLength(16)]
    public string? DistanceUnit { get; set; }
    
    public decimal? AverageSpeed { get; set; }
    
    public int? AverageHeartRate { get; set; }
    
    public int? MaxHeartRate { get; set; }
    
    public decimal? ElevationGain { get; set; }
    
    public decimal? MaxSpeed { get; set; }


    [MaxLength(4096)]
    public string? Notes { get; set; }

    public FitnessVisibility Visibility { get; set; } = FitnessVisibility.Private;
}
