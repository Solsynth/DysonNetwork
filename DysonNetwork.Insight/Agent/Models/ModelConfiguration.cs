using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

namespace DysonNetwork.Insight.Agent.Models;

/// <summary>
/// Configuration for a specific model instance.
/// Allows per-use overrides of model parameters while maintaining a reference to the base model.
/// Supports custom providers with custom base URLs for OpenAI-compatible APIs.
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
    /// Custom provider name (e.g., "together", "fireworks", "custom").
    /// Overrides the provider from ModelRegistry. Use for custom OpenAI-compatible providers.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Custom base URL for the API endpoint (e.g., "https://api.together.xyz/v1").
    /// Overrides the base URL from ModelRegistry. Use for custom OpenAI-compatible providers.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Custom API key for the provider.
    /// If set, overrides the API key from configuration or ModelRegistry.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Custom model name to use with the provider.
    /// If set, overrides the model name from ModelRegistry.
    /// </summary>
    public string? CustomModelName { get; set; }

    /// <summary>
    /// Transport API mode. Supported values: "responses" and "chat".
    /// Defaults to "chat".
    /// </summary>
    public string? ApiMode { get; set; }

    /// <summary>
    /// Custom parameters specific to this configuration
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// Gets the effective temperature (override or model default)
    /// </summary>
    public double GetEffectiveTemperature(ModelRegistry? modelRegistry = null)
    {
        if (Temperature.HasValue)
            return Temperature.Value;

        var model = modelRegistry?.GetById(ModelId);
        return model?.DefaultTemperature ?? 0.7;
    }

    /// <summary>
    /// Gets the effective reasoning effort (override or model default)
    /// </summary>
    public string? GetEffectiveReasoningEffort(ModelRegistry? modelRegistry = null)
    {
        if (!string.IsNullOrEmpty(ReasoningEffort))
            return ReasoningEffort;

        var model = modelRegistry?.GetById(ModelId);
        return model?.DefaultReasoningEffort;
    }

    /// <summary>
    /// Gets the ModelRef for this configuration with custom overrides applied
    /// </summary>
    public ModelRef? GetModelRef(ModelRegistry? modelRegistry = null)
    {
        var modelRef = modelRegistry?.GetById(ModelId);
        if (modelRef == null) return null;

        // Apply custom overrides if specified
        if (!string.IsNullOrEmpty(BaseUrl))
            modelRef = modelRef.WithBaseUrl(BaseUrl);
        if (!string.IsNullOrEmpty(ApiKey))
            modelRef = modelRef.WithApiKey(ApiKey);

        return modelRef;
    }

    /// <summary>
    /// Gets the effective provider name (custom override or from ModelRegistry)
    /// </summary>
    public string GetEffectiveProvider(ModelRegistry? modelRegistry = null)
    {
        if (!string.IsNullOrEmpty(Provider))
            return Provider;

        var modelRef = modelRegistry?.GetById(ModelId);
        return modelRef?.Provider ?? "openrouter";
    }

    /// <summary>
    /// Gets the effective model name (custom override or from ModelRegistry)
    /// </summary>
    public string GetEffectiveModelName(ModelRegistry? modelRegistry = null)
    {
        if (!string.IsNullOrEmpty(CustomModelName))
            return CustomModelName;

        var modelRef = modelRegistry?.GetById(ModelId);
        return modelRef?.ModelName ?? ModelId;
    }

    /// <summary>
    /// Gets the effective base URL (custom override or from ModelRegistry)
    /// </summary>
    public string? GetEffectiveBaseUrl(ModelRegistry? modelRegistry = null)
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;

        var modelRef = modelRegistry?.GetById(ModelId);
        return modelRef?.BaseUrl;
    }

    /// <summary>
    /// Gets the effective API key (custom override or from ModelRegistry)
    /// </summary>
    public string? GetEffectiveApiKey(ModelRegistry? modelRegistry = null)
    {
        if (!string.IsNullOrEmpty(ApiKey))
            return ApiKey;

        var modelRef = modelRegistry?.GetById(ModelId);
        return modelRef?.ApiKey;
    }

    /// <summary>
    /// Gets the effective API mode (override or default).
    /// </summary>
    public string GetEffectiveApiMode()
    {
        return string.IsNullOrWhiteSpace(ApiMode) ? "chat" : ApiMode.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Validates this configuration
    /// </summary>
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

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

        if (!string.IsNullOrWhiteSpace(ApiMode))
        {
            var validApiModes = new[] { "responses", "chat" };
            if (!validApiModes.Contains(ApiMode.Trim().ToLowerInvariant()))
            {
                results.Add(new ValidationResult(
                    "ApiMode must be one of: responses, chat",
                    new[] { nameof(ApiMode) }));
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
            Provider = Provider,
            BaseUrl = BaseUrl,
            ApiKey = ApiKey,
            CustomModelName = CustomModelName,
            ApiMode = ApiMode,
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

    public override string ToString()
    {
        var parts = new List<string> { ModelId };

        if (Temperature.HasValue)
            parts.Add($"temp: {Temperature.Value}");
        if (!string.IsNullOrEmpty(Provider))
            parts.Add($"provider: {Provider}");
        if (!string.IsNullOrEmpty(BaseUrl))
            parts.Add($"url: {BaseUrl}");

        return string.Join(" | ", parts);
    }
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

    /// <summary>
    /// Sets a custom provider fluently (for OpenAI-compatible APIs)
    /// </summary>
    public static ModelConfiguration WithProvider(this ModelConfiguration config, string provider)
    {
        config.Provider = provider;
        return config;
    }

    /// <summary>
    /// Sets a custom base URL fluently (for OpenAI-compatible APIs)
    /// </summary>
    public static ModelConfiguration WithBaseUrl(this ModelConfiguration config, string baseUrl)
    {
        config.BaseUrl = baseUrl;
        return config;
    }

    /// <summary>
    /// Sets a custom API key fluently (for OpenAI-compatible APIs)
    /// </summary>
    public static ModelConfiguration WithApiKey(this ModelConfiguration config, string apiKey)
    {
        config.ApiKey = apiKey;
        return config;
    }

    /// <summary>
    /// Sets a custom model name fluently (overrides the model name from ModelRegistry)
    /// </summary>
    public static ModelConfiguration WithCustomModelName(this ModelConfiguration config, string modelName)
    {
        config.CustomModelName = modelName;
        return config;
    }

    /// <summary>
    /// Configures this model to use a custom OpenAI-compatible provider
    /// </summary>
    public static ModelConfiguration WithCustomProvider(
        this ModelConfiguration config,
        string baseUrl,
        string? apiKey = null,
        string? providerName = "custom",
        string? modelName = null)
    {
        config.Provider = providerName;
        config.BaseUrl = baseUrl;
        if (apiKey != null)
            config.ApiKey = apiKey;
        if (modelName != null)
            config.CustomModelName = modelName;
        return config;
    }
}
