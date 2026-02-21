using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Fitness.ExerciseLibrary;

public class SnExerciseLibrary : ModelBase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string? Description { get; set; }

    [Required]
    public ExerciseCategory Category { get; set; } = ExerciseCategory.Other;

    [Column(TypeName = "jsonb")]
    public List<string>? MuscleGroups { get; set; }

    [Required]
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Beginner;

    [Column(TypeName = "jsonb")]
    public List<string>? Equipment { get; set; }

    [Required]
    public bool IsPublic { get; set; } = true;

    public Guid? AccountId { get; set; }
}
