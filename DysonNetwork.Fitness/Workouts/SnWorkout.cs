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

    [NotMapped]
    [JsonIgnore]
    public decimal? Distance
    {
        get => GetMetaDecimal("distance");
        set => SetMetaDecimal("distance", value);
    }

    [NotMapped]
    [JsonIgnore]
    public string? DistanceUnit
    {
        get => GetMetaString("distance_unit");
        set => SetMetaString("distance_unit", value);
    }

    [NotMapped]
    [JsonIgnore]
    public decimal? AverageSpeed
    {
        get => GetMetaDecimal("average_speed");
        set => SetMetaDecimal("average_speed", value);
    }

    [NotMapped]
    [JsonIgnore]
    public int? AverageHeartRate
    {
        get => GetMetaInt("average_heart_rate");
        set => SetMetaInt("average_heart_rate", value);
    }

    [NotMapped]
    [JsonIgnore]
    public int? MaxHeartRate
    {
        get => GetMetaInt("max_heart_rate");
        set => SetMetaInt("max_heart_rate", value);
    }

    [NotMapped]
    [JsonIgnore]
    public decimal? ElevationGain
    {
        get => GetMetaDecimal("elevation_gain");
        set => SetMetaDecimal("elevation_gain", value);
    }

    [NotMapped]
    [JsonIgnore]
    public decimal? MaxSpeed
    {
        get => GetMetaDecimal("max_speed");
        set => SetMetaDecimal("max_speed", value);
    }

    private decimal? GetMetaDecimal(string key)
    {
        if (Meta == null) return null;
        using var doc = JsonDocument.Parse(Meta.RootElement.GetRawText());
        if (doc.RootElement.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.Number)
            return element.GetDecimal();
        return null;
    }

    private string? GetMetaString(string key)
    {
        if (Meta == null) return null;
        using var doc = JsonDocument.Parse(Meta.RootElement.GetRawText());
        if (doc.RootElement.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.String)
            return element.GetString();
        return null;
    }

    private int? GetMetaInt(string key)
    {
        if (Meta == null) return null;
        using var doc = JsonDocument.Parse(Meta.RootElement.GetRawText());
        if (doc.RootElement.TryGetProperty(key, out var element) && element.ValueKind == JsonValueKind.Number)
            return element.GetInt32();
        return null;
    }

    private void SetMetaDecimal(string key, decimal? value)
    {
        var dict = GetMetaDictionary();
        if (value.HasValue)
            dict[key] = value.Value;
        else
            dict.Remove(key);
        Meta = JsonDocument.Parse(JsonSerializer.Serialize(dict));
    }

    private void SetMetaString(string key, string? value)
    {
        var dict = GetMetaDictionary();
        if (!string.IsNullOrEmpty(value))
            dict[key] = value;
        else
            dict.Remove(key);
        Meta = JsonDocument.Parse(JsonSerializer.Serialize(dict));
    }

    private void SetMetaInt(string key, int? value)
    {
        var dict = GetMetaDictionary();
        if (value.HasValue)
            dict[key] = value.Value;
        else
            dict.Remove(key);
        Meta = JsonDocument.Parse(JsonSerializer.Serialize(dict));
    }

    private Dictionary<string, object> GetMetaDictionary()
    {
        if (Meta == null) return new Dictionary<string, object>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(Meta.RootElement.GetRawText()) ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    [MaxLength(4096)]
    public string? Notes { get; set; }

    public FitnessVisibility Visibility { get; set; } = FitnessVisibility.Private;

    [JsonIgnore]
    public List<SnWorkoutExercise> Exercises { get; set; } = [];
}
