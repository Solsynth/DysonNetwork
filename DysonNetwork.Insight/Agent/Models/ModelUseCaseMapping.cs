using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Insight.Agent.Models;

/// <summary>
/// Maps a model to a specific use case with PerkLevel requirements.
/// Allows different users to access different models based on their subscription level.
/// </summary>
public class ModelUseCaseMapping
{
    /// <summary>
    /// The use case this mapping applies to
    /// </summary>
    [Required]
    public ModelUseCase UseCase { get; set; } = ModelUseCase.Default;

    /// <summary>
    /// The model ID from ModelRegistry (e.g., "deepseek-chat")
    /// </summary>
    [Required]
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Minimum PerkLevel required to use this model for this use case.
    /// 0 = available to all users.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int MinPerkLevel { get; set; } = 0;

    /// <summary>
    /// Maximum PerkLevel that can use this model (optional, for tiered access).
    /// Null means no maximum.
    /// </summary>
    public int? MaxPerkLevel { get; set; }

    /// <summary>
    /// Whether this is the default model for this use case (used when user has no preference or insufficient perk level)
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Priority for model selection when multiple models match (higher = preferred)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Display name for this mapping (optional, for UI)
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of when to use this model (optional, for UI/help)
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this mapping is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Custom model configuration overrides for this use case
    /// </summary>
    public ModelConfiguration? ConfigurationOverrides { get; set; }

    /// <summary>
    /// Checks if a user with the given PerkLevel can use this model
    /// </summary>
    public bool CanUse(int userPerkLevel)
    {
        if (!Enabled) return false;
        if (userPerkLevel < MinPerkLevel) return false;
        if (MaxPerkLevel.HasValue && userPerkLevel > MaxPerkLevel.Value) return false;
        return true;
    }

    /// <summary>
    /// Gets the effective model configuration for this mapping
    /// </summary>
    public ModelConfiguration GetModelConfiguration()
    {
        var baseConfig = ConfigurationOverrides ?? new ModelConfiguration();
        baseConfig.ModelId = ModelId;
        return baseConfig;
    }

    public override string ToString() =>
        $"{UseCase.GetDisplayName()} -> {ModelId} (Perk {MinPerkLevel}+)";
}

/// <summary>
/// Configuration for model selection across all use cases
/// </summary>
public class ModelSelectionConfig
{
    /// <summary>
    /// All model mappings for different use cases
    /// </summary>
    public List<ModelUseCaseMapping> Mappings { get; set; } = new();

    /// <summary>
    /// Default model to use when no mapping matches
    /// </summary>
    public string DefaultModelId { get; set; } = "deepseek-chat";

    /// <summary>
    /// Whether to allow users to override model selection (if they have sufficient PerkLevel)
    /// </summary>
    public bool AllowUserOverride { get; set; } = true;

    /// <summary>
    /// Gets all mappings for a specific use case
    /// </summary>
    public IEnumerable<ModelUseCaseMapping> GetMappingsForUseCase(ModelUseCase useCase)
    {
        return Mappings
            .Where(m => m.UseCase == useCase && m.Enabled)
            .OrderByDescending(m => m.Priority);
    }

    /// <summary>
    /// Gets available models for a use case and PerkLevel
    /// </summary>
    public IEnumerable<ModelUseCaseMapping> GetAvailableModels(ModelUseCase useCase, int perkLevel)
    {
        return GetMappingsForUseCase(useCase)
            .Where(m => m.CanUse(perkLevel));
    }

    /// <summary>
    /// Gets the default model for a use case (highest priority that user can access)
    /// </summary>
    public ModelUseCaseMapping? GetDefaultMapping(ModelUseCase useCase, int perkLevel)
    {
        var available = GetAvailableModels(useCase, perkLevel);

        // First try to find a mapping explicitly marked as default
        var explicitDefault = available.FirstOrDefault(m => m.IsDefault);
        if (explicitDefault != null) return explicitDefault;

        // Otherwise, return the highest priority available model
        return available.FirstOrDefault();
    }

    /// <summary>
    /// Gets a specific model mapping if the user has access
    /// </summary>
    public ModelUseCaseMapping? GetMappingIfAllowed(ModelUseCase useCase, string modelId, int perkLevel)
    {
        var mapping = Mappings.FirstOrDefault(m =>
            m.UseCase == useCase &&
            m.ModelId == modelId &&
            m.Enabled);

        return mapping?.CanUse(perkLevel) == true ? mapping : null;
    }
}
