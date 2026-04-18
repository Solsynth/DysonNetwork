using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Insight.Agent.Models;

/// <summary>
/// Configuration for a specific model instance.
/// Allows per-use overrides of model parameters while maintaining a reference to the base model.
/// </summary>
public class ModelConfiguration
{
    /// <summary>
    /// The model ID from ModelRegistry (e.g., "deepseek-chat")
    /// </summary>
    [Required(ErrorMessage = "ModelId is required")]
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Override temperature. If null, uses the model's default.
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Override reasoning effort (low/medium/high). If null, uses the model's default.
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// Maximum tokens for this configuration. If null, uses provider default.
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Whether to enable function calling for this model
    /// </summary>
    public bool EnableFunctions { get; set; } = true;

    /// <summary>
    /// Whether this model can be switched at runtime
    /// </summary>
    public bool AllowRuntimeSwitch { get; set; } = true;

    /// <summary>
    /// Custom parameters specific to this configuration
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Gets the effective temperature (override or model default)
    /// </summary>
    public double GetEffectiveTemperature()
    {
        if (Temperature.HasValue)
            return Temperature.Value;

        var model = ModelRegistry.GetById(ModelId);
        return model?.DefaultTemperature ?? 0.7;
    }

    /// <summary>
    /// Gets the effective reasoning effort (override or model default)
    /// </summary>
    public string? GetEffectiveReasoningEffort()
    {
        if (!string.IsNullOrEmpty(ReasoningEffort))
            return ReasoningEffort;

        var model = ModelRegistry.GetById(ModelId);
        return model?.DefaultReasoningEffort;
    }

    /// <summary>
    /// Gets the ModelRef for this configuration
    /// </summary>
    public ModelRef? GetModelRef() => ModelRegistry.GetById(ModelId);

    /// <summary>
    /// Validates this configuration
    /// </summary>
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        // Validate ModelId exists in registry
        if (!string.IsNullOrEmpty(ModelId) && !ModelRegistry.IsValid(ModelId))
        {
            results.Add(new ValidationResult(
                $"ModelId '{ModelId}' is not registered in ModelRegistry. " +
                $"Available models: {string.Join(", ", ModelRegistry.All.Select(m => m.Id))}",
                new[] { nameof(ModelId) }));
        }

        // Validate temperature range
        if (Temperature.HasValue && (Temperature.Value < 0 || Temperature.Value > 2))
        {
            results.Add(new ValidationResult(
                "Temperature must be between 0 and 2",
                new[] { nameof(Temperature) }));
        }

        // Validate reasoning effort
        if (!string.IsNullOrEmpty(ReasoningEffort))
        {
            var validEfforts = new[] { "low", "medium", "high" };
            if (!validEfforts.Contains(ReasoningEffort.ToLower()))
            {
                results.Add(new ValidationResult(
                    "ReasoningEffort must be one of: low, medium, high",
                    new[] { nameof(ReasoningEffort) }));
            }
        }

        return results;
    }

    /// <summary>
    /// Creates a clone of this configuration with optional overrides
    /// </summary>
    public ModelConfiguration Clone(Action<ModelConfiguration>? configure = null)
    {
        var clone = new ModelConfiguration
        {
            ModelId = ModelId,
            Temperature = Temperature,
            ReasoningEffort = ReasoningEffort,
            MaxTokens = MaxTokens,
            EnableFunctions = EnableFunctions,
            AllowRuntimeSwitch = AllowRuntimeSwitch,
            Parameters = new Dictionary<string, object>(Parameters)
        };

        configure?.Invoke(clone);
        return clone;
    }

    /// <summary>
    /// Implicit conversion from string for simple cases
    /// </summary>
    public static implicit operator ModelConfiguration(string modelId) =>
        new() { ModelId = modelId };

    /// <summary>
    /// Implicit conversion from ModelRef
    /// </summary>
    public static implicit operator ModelConfiguration(ModelRef modelRef) =>
        new() { ModelId = modelRef.Id };

    public override string ToString() =>
        $"{ModelId}" + (Temperature.HasValue ? $" (temp: {Temperature.Value})" : "");
}

/// <summary>
/// A model configuration that falls back to another configuration if not explicitly set
/// </summary>
public class FallbackModelConfiguration : ModelConfiguration
{
    private readonly Func<ModelConfiguration> _fallbackProvider;

    public FallbackModelConfiguration(Func<ModelConfiguration> fallbackProvider)
    {
        _fallbackProvider = fallbackProvider;
    }

    /// <summary>
    /// Gets the effective model ID (falls back if not set)
    /// </summary>
    public string EffectiveModelId =>
        !string.IsNullOrEmpty(ModelId) ? ModelId : _fallbackProvider().ModelId;

    /// <summary>
    /// Gets the effective temperature (falls back if not set)
    /// </summary>
    public double EffectiveTemperature =>
        Temperature ?? _fallbackProvider().GetEffectiveTemperature();

    public override string ToString() =>
        $"{EffectiveModelId}" + (EffectiveTemperature != 0.7 ? $" (temp: {EffectiveTemperature})" : "") + " [fallback]";
}

/// <summary>
/// Extension methods for ModelConfiguration
/// </summary>
public static class ModelConfigurationExtensions
{
    /// <summary>
    /// Sets the temperature fluently
    /// </summary>
    public static ModelConfiguration WithTemperature(this ModelConfiguration config, double temperature)
    {
        config.Temperature = temperature;
        return config;
    }

    /// <summary>
    /// Sets the reasoning effort fluently
    /// </summary>
    public static ModelConfiguration WithReasoningEffort(this ModelConfiguration config, string effort)
    {
        config.ReasoningEffort = effort;
        return config;
    }

    /// <summary>
    /// Sets the max tokens fluently
    /// </summary>
    public static ModelConfiguration WithMaxTokens(this ModelConfiguration config, int maxTokens)
    {
        config.MaxTokens = maxTokens;
        return config;
    }

    /// <summary>
    /// Disables function calling fluently
    /// </summary>
    public static ModelConfiguration WithoutFunctions(this ModelConfiguration config)
    {
        config.EnableFunctions = false;
        return config;
    }

    /// <summary>
    /// Adds a custom parameter fluently
    /// </summary>
    public static ModelConfiguration WithParameter(this ModelConfiguration config, string key, object value)
    {
        config.Parameters[key] = value;
        return config;
    }
}
