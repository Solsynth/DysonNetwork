using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Fitness.Workouts;

public class SnWorkout : ModelBase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

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

    [MaxLength(4096)]
    public string? Notes { get; set; }

    [JsonIgnore]
    public List<SnWorkoutExercise> Exercises { get; set; } = [];
}
