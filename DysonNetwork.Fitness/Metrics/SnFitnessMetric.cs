using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Fitness.Metrics;

public class SnFitnessMetric : ModelBase
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid AccountId { get; set; }

    [Required]
    public FitnessMetricType MetricType { get; set; } = FitnessMetricType.Custom;

    [Required]
    [Column(TypeName = "decimal(10,2)")]
    public decimal Value { get; set; }

    [Required]
    [MaxLength(32)]
    public string Unit { get; set; } = string.Empty;

    [Required]
    public Instant RecordedAt { get; set; }

    [MaxLength(4096)]
    public string? Notes { get; set; }

    [MaxLength(256)]
    public string? Source { get; set; }
}
