using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Fitness.Workouts;

public class SnWorkoutExercise : ModelBase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid WorkoutId { get; set; }

    [JsonIgnore]
    public SnWorkout Workout { get; set; } = null!;

    [Required]
    [MaxLength(256)]
    public string ExerciseName { get; set; } = string.Empty;

    public int? Sets { get; set; }

    public int? Reps { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Weight { get; set; }

    public Duration? Duration { get; set; }

    [MaxLength(4096)]
    public string? Notes { get; set; }

    [Required]
    public int OrderIndex { get; set; } = 0;
}
