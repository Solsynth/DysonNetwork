using Microsoft.Extensions.Options;

namespace DysonNetwork.Insight.Agent.Models;

/// <summary>
/// Implementation of IModelSelector that uses ModelSelectionConfig to choose appropriate models
/// based on use case and user PerkLevel.
/// </summary>
public class ModelSelector : IModelSelector
{
    private readonly ModelSelectionConfig _config;
    private readonly ILogger<ModelSelector> _logger;
    private readonly ModelRegistry _modelRegistry;

    public ModelSelector(
        IOptions<ModelSelectionConfig> config,
        ILogger<ModelSelector> logger,
        ModelRegistry modelRegistry)
    {
        _config = config.Value;
        _logger = logger;
        _modelRegistry = modelRegistry;
    }

    /// <summary>
    /// Selects the best model for the given context
    /// </summary>
    public ModelSelectionResult SelectModel(UserModelContext context)
    {
        var availableModels = GetAvailableModels(context.UseCase, context.PerkLevel).ToList();

        if (!availableModels.Any())
        {
            _logger.LogWarning(
                "No models available for use case {UseCase} with PerkLevel {PerkLevel}",
                context.UseCase, context.PerkLevel);

            // Try to fall back to default model
            var defaultModel = _modelRegistry.GetById(_config.DefaultModelId);
            if (defaultModel != null)
            {
                _logger.LogInformation("Falling back to default model {ModelId}", _config.DefaultModelId);
                return ModelSelectionResult.Successful(
                    new ModelConfiguration { ModelId = _config.DefaultModelId },
                    new ModelUseCaseMapping
                    {
                        UseCase = context.UseCase,
                        ModelId = _config.DefaultModelId,
                        MinPerkLevel = 0,
                        IsDefault = true
                    },
                    availableModels,
                    usedPreferred: false);
            }

            return ModelSelectionResult.Failed(
                $"No models available for {context.UseCase.GetDisplayName()} at PerkLevel {context.PerkLevel}");
        }

        // Check if user has a preferred model and can use it
        if (!string.IsNullOrEmpty(context.PreferredModelId) && _config.AllowUserOverride)
        {
            var preferredMapping = availableModels.FirstOrDefault(m => m.ModelId == context.PreferredModelId);
            if (preferredMapping != null)
            {
                _logger.LogDebug(
                    "Using user's preferred model {ModelId} for {UseCase}",
                    context.PreferredModelId, context.UseCase);

                var config = ApplyContextOverrides(preferredMapping.GetModelConfiguration(), context);
                return ModelSelectionResult.Successful(config, preferredMapping, availableModels, usedPreferred: true);
            }

            _logger.LogWarning(
                "User requested model {ModelId} for {UseCase} but doesn't have access (PerkLevel {PerkLevel})",
                context.PreferredModelId, context.UseCase, context.PerkLevel);
        }

        // Use best available model (highest priority)
        var bestMapping = context.UseBestAvailable
            ? availableModels.First()
            : availableModels.FirstOrDefault(m => m.IsDefault) ?? availableModels.First();

        _logger.LogDebug(
            "Selected model {ModelId} for {UseCase} (PerkLevel {PerkLevel}, Priority {Priority})",
            bestMapping.ModelId, context.UseCase, context.PerkLevel, bestMapping.Priority);

        var modelConfig = ApplyContextOverrides(bestMapping.GetModelConfiguration(), context);
        return ModelSelectionResult.Successful(modelConfig, bestMapping, availableModels);
    }

    /// <summary>
    /// Selects a model for a specific use case with PerkLevel
    /// </summary>
    public ModelSelectionResult SelectModel(ModelUseCase useCase, int perkLevel = 0, string? preferredModelId = null)
    {
        return SelectModel(new UserModelContext
        {
            UseCase = useCase,
            PerkLevel = perkLevel,
            PreferredModelId = preferredModelId
        });
    }

    /// <summary>
    /// Gets all available models for a use case and PerkLevel
    /// </summary>
    public IEnumerable<ModelUseCaseMapping> GetAvailableModels(ModelUseCase useCase, int perkLevel)
    {
        // Superusers can access all models
        var effectivePerkLevel = perkLevel;

        return _config.GetAvailableModels(useCase, effectivePerkLevel);
    }

    /// <summary>
    /// Checks if a user can access a specific model for a use case
    /// </summary>
    public bool CanAccessModel(ModelUseCase useCase, string modelId, int perkLevel)
    {
        return _config.GetMappingIfAllowed(useCase, modelId, perkLevel) != null;
    }

    /// <summary>
    /// Gets the default model configuration for a use case
    /// </summary>
    public ModelConfiguration GetDefaultConfiguration(ModelUseCase useCase, int perkLevel = 0)
    {
        var result = SelectModel(useCase, perkLevel);
        return result.Configuration ?? new ModelConfiguration { ModelId = _config.DefaultModelId };
    }

    /// <summary>
    /// Applies context-specific overrides to the model configuration
    /// </summary>
    private ModelConfiguration ApplyContextOverrides(ModelConfiguration config, UserModelContext context)
    {
        if (context.Temperature.HasValue)
        {
            config.Temperature = context.Temperature.Value;
        }

        if (!string.IsNullOrEmpty(context.ReasoningEffort))
        {
            config.ReasoningEffort = context.ReasoningEffort;
        }

        return config;
    }
}

/// <summary>
/// Extension methods for IModelSelector
/// </summary>
public static class ModelSelectorExtensions
{
    /// <summary>
    /// Selects a model and throws if no model is available
    /// </summary>
    public static ModelConfiguration SelectModelOrThrow(
        this IModelSelector selector,
        UserModelContext context)
    {
        var result = selector.SelectModel(context);
        if (!result.Success || result.Configuration == null)
        {
            throw new InvalidOperationException(
                result.ErrorMessage ?? "Failed to select a model for the given context");
        }
        return result.Configuration;
    }

    /// <summary>
    /// Selects a model for MiChan chat
    /// </summary>
    public static ModelSelectionResult SelectMiChanModel(
        this IModelSelector selector,
        int perkLevel = 0,
        string? preferredModelId = null) =>
        selector.SelectModel(ModelUseCase.MiChanChat, perkLevel, preferredModelId);

    /// <summary>
    /// Selects a model for MiChan autonomous behavior
    /// </summary>
    public static ModelSelectionResult SelectMiChanAutonomousModel(
        this IModelSelector selector,
        int perkLevel = 0) =>
        selector.SelectModel(ModelUseCase.MiChanAutonomous, perkLevel);

    /// <summary>
    /// Selects a model for SN-chan chat
    /// </summary>
    public static ModelSelectionResult SelectSnChanModel(
        this IModelSelector selector,
        int perkLevel = 0,
        string? preferredModelId = null) =>
        selector.SelectModel(ModelUseCase.SnChanChat, perkLevel, preferredModelId);
}
