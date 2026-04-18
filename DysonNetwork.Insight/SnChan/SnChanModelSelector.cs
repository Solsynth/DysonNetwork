using DysonNetwork.Insight.Agent.Models;

namespace DysonNetwork.Insight.SnChan;

/// <summary>
/// Model selector specifically for SN-chan services.
/// Uses SnChanConfig for model selection based on PerkLevel.
/// </summary>
public class SnChanModelSelector
{
    private readonly SnChanConfig _config;
    private readonly ILogger<SnChanModelSelector> _logger;

    public SnChanModelSelector(
        SnChanConfig config,
        ILogger<SnChanModelSelector> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Selects the best model for the given use case and PerkLevel
    /// </summary>
    public SnChanModelSelectionResult SelectModel(ModelUseCase useCase, int perkLevel = 0, string? preferredModelId = null)
    {
        var availableModels = GetAvailableModels(useCase, perkLevel).ToList();

        if (!availableModels.Any())
        {
            _logger.LogWarning(
                "No models available for SN-chan use case {UseCase} with PerkLevel {PerkLevel}",
                useCase, perkLevel);

            return SnChanModelSelectionResult.Failed(
                $"No models available for {useCase.GetDisplayName()} at PerkLevel {perkLevel}");
        }

        // Check if user has a preferred model and can use it
        if (!string.IsNullOrEmpty(preferredModelId) && _config.ModelSelection.AllowUserOverride)
        {
            var preferredMapping = availableModels.FirstOrDefault(m => m.ModelId == preferredModelId);
            if (preferredMapping != null)
            {
                _logger.LogDebug(
                    "Using user's preferred model {ModelId} for SN-chan {UseCase}",
                    preferredModelId, useCase);

                return SnChanModelSelectionResult.Successful(
                    preferredMapping,
                    availableModels,
                    usedPreferred: true);
            }

            _logger.LogWarning(
                "User requested model {ModelId} for SN-chan {UseCase} but doesn't have access (PerkLevel {PerkLevel})",
                preferredModelId, useCase, perkLevel);
        }

        // Use best available model (highest priority)
        var bestMapping = availableModels.First();

        _logger.LogDebug(
            "Selected model {ModelId} for SN-chan {UseCase} (PerkLevel {PerkLevel}, Priority {Priority})",
            bestMapping.ModelId, useCase, perkLevel, bestMapping.Priority);

        return SnChanModelSelectionResult.Successful(bestMapping, availableModels);
    }

    /// <summary>
    /// Gets all available models for a use case and PerkLevel
    /// </summary>
    public IEnumerable<SnChanModelMapping> GetAvailableModels(ModelUseCase useCase, int perkLevel)
    {
        return _config.ModelSelection.GetAvailableModels(useCase, perkLevel);
    }

    /// <summary>
    /// Checks if a user can access a specific model for a use case
    /// </summary>
    public bool CanAccessModel(ModelUseCase useCase, string modelId, int perkLevel)
    {
        var mapping = _config.ModelSelection.Mappings.FirstOrDefault(m =>
            m.UseCase == useCase &&
            m.ModelId == modelId &&
            m.Enabled);

        return mapping?.CanUse(perkLevel) == true;
    }

    /// <summary>
    /// Gets the model configuration for a use case
    /// </summary>
    public Agent.Models.ModelConfiguration GetModelConfiguration(ModelUseCase useCase, int perkLevel = 0, string? preferredModelId = null)
    {
        return _config.GetModelForUseCase(useCase, perkLevel, preferredModelId);
    }
}

/// <summary>
/// Result of a SN-chan model selection operation
/// </summary>
public class SnChanModelSelectionResult
{
    /// <summary>
    /// Whether a suitable model was found
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The selected model mapping
    /// </summary>
    public SnChanModelMapping? Mapping { get; set; }

    /// <summary>
    /// Error message if selection failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the user's preferred model was used
    /// </summary>
    public bool UsedPreferredModel { get; set; }

    /// <summary>
    /// Available models for the user's PerkLevel
    /// </summary>
    public List<SnChanModelMapping> AvailableModels { get; set; } = new();

    public static SnChanModelSelectionResult Successful(
        SnChanModelMapping mapping,
        List<SnChanModelMapping> available,
        bool usedPreferred = false) => new()
    {
        Success = true,
        Mapping = mapping,
        AvailableModels = available,
        UsedPreferredModel = usedPreferred
    };

    public static SnChanModelSelectionResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Extension methods for SnChanModelSelector
/// </summary>
public static class SnChanModelSelectorExtensions
{
    /// <summary>
    /// Selects a model for SN-chan chat
    /// </summary>
    public static SnChanModelSelectionResult SelectSnChanChatModel(
        this SnChanModelSelector selector,
        int perkLevel = 0,
        string? preferredModelId = null) =>
        selector.SelectModel(ModelUseCase.SnChanChat, perkLevel, preferredModelId);

    /// <summary>
    /// Selects a model for SN-chan reasoning
    /// </summary>
    public static SnChanModelSelectionResult SelectSnChanReasoningModel(
        this SnChanModelSelector selector,
        int perkLevel = 0) =>
        selector.SelectModel(ModelUseCase.SnChanReasoning, perkLevel);

    /// <summary>
    /// Selects a model for SN-chan vision
    /// </summary>
    public static SnChanModelSelectionResult SelectSnChanVisionModel(
        this SnChanModelSelector selector,
        int perkLevel = 0) =>
        selector.SelectModel(ModelUseCase.SnChanVision, perkLevel);
}
