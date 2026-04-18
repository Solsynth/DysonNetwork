using System.ComponentModel.DataAnnotations;
using DysonNetwork.Insight.Agent.Models;

namespace DysonNetwork.Insight.SnChan;

/// <summary>
/// Configuration for SN-chan (Thought) AI services
/// </summary>
public class SnChanConfig : IValidatableObject
{
    /// <summary>
    /// Whether to use the new model selection system based on use cases and PerkLevel.
    /// When enabled, ModelSelection configuration is used instead of direct model properties.
    /// </summary>
    public bool UseModelSelection { get; set; } = false;

    /// <summary>
    /// Model selection configuration for different use cases.
    /// Only used when UseModelSelection is true.
    /// </summary>
    public SnChanModelSelectionConfig ModelSelection { get; set; } = new();

    /// <summary>
    /// Default model for SN-chan chat when model selection is disabled.
    /// Falls back to Thinking:DefaultService if not set.
    /// </summary>
    public ModelConfiguration? DefaultChatModel { get; set; }

    /// <summary>
    /// Default model for SN-chan reasoning tasks.
    /// Falls back to DefaultChatModel if not set.
    /// </summary>
    public ModelConfiguration? ReasoningModel { get; set; }

    /// <summary>
    /// Default model for SN-chan vision tasks.
    /// Falls back to DefaultChatModel if not set.
    /// </summary>
    public ModelConfiguration? VisionModel { get; set; }

    /// <summary>
    /// Whether to enable vision analysis for SN-chan
    /// </summary>
    public bool EnableVision { get; set; } = true;

    /// <summary>
    /// Maximum number of images to process in a single request
    /// </summary>
    public int MaxImagesPerRequest { get; set; } = 10;

    /// <summary>
    /// Whether to fallback to text-only model if vision model is unavailable
    /// </summary>
    public bool FallbackToTextModel { get; set; } = true;

    /// <summary>
    /// Gets the effective default chat model
    /// </summary>
    public ModelConfiguration GetDefaultChatModel(string? defaultServiceId = null) =>
        DefaultChatModel ?? new ModelConfiguration { ModelId = defaultServiceId ?? ModelRegistry.DeepSeekChat.Id };

    /// <summary>
    /// Gets the effective reasoning model
    /// </summary>
    public ModelConfiguration GetReasoningModel(string? defaultServiceId = null) =>
        ReasoningModel ?? DefaultChatModel ?? new ModelConfiguration { ModelId = defaultServiceId ?? ModelRegistry.DeepSeekChat.Id };

    /// <summary>
    /// Gets the effective vision model
    /// </summary>
    public ModelConfiguration GetVisionModel(string? defaultServiceId = null) =>
        VisionModel ?? DefaultChatModel ?? new ModelConfiguration { ModelId = defaultServiceId ?? ModelRegistry.DeepSeekChat.Id };

    /// <summary>
    /// Gets the model for a specific use case based on PerkLevel
    /// </summary>
    public ModelConfiguration GetModelForUseCase(ModelUseCase useCase, int perkLevel, string? preferredModelId = null)
    {
        if (!UseModelSelection)
        {
            return useCase switch
            {
                ModelUseCase.SnChanReasoning => GetReasoningModel(),
                ModelUseCase.SnChanVision => GetVisionModel(),
                _ => GetDefaultChatModel()
            };
        }

        var selection = ModelSelection;

        // Check if user has a preferred model and can use it
        if (!string.IsNullOrEmpty(preferredModelId) && selection.AllowUserOverride)
        {
            var preferredMapping = selection.Mappings.FirstOrDefault(m =>
                m.UseCase == useCase &&
                m.ModelId == preferredModelId &&
                m.Enabled &&
                m.MinPerkLevel <= perkLevel);

            if (preferredMapping != null)
            {
                return new ModelConfiguration { ModelId = preferredMapping.ModelId };
            }
        }

        // Get default mapping for this use case
        var defaultMapping = selection.GetDefaultMapping(useCase, perkLevel);
        if (defaultMapping != null)
        {
            return new ModelConfiguration { ModelId = defaultMapping.ModelId };
        }

        // Fallback
        return useCase switch
        {
            ModelUseCase.SnChanReasoning => GetReasoningModel(),
            ModelUseCase.SnChanVision => GetVisionModel(),
            _ => GetDefaultChatModel()
        };
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        // Validate model selection configuration if enabled
        if (UseModelSelection && ModelSelection?.Mappings != null)
        {
            foreach (var mapping in ModelSelection.Mappings.Where(m => m.Enabled))
            {
                if (string.IsNullOrEmpty(mapping.ModelId))
                {
                    results.Add(new ValidationResult(
                        "ModelId is required for enabled mapping",
                        new[] { nameof(ModelSelection) + ".Mappings" }));
                }
            }
        }

        return results;
    }
}

/// <summary>
/// Model selection configuration for SN-chan with PerkLevel-based access control
/// </summary>
public class SnChanModelSelectionConfig
{
    /// <summary>
    /// Default model for users with PerkLevel 0
    /// </summary>
    public string DefaultModelId { get; set; } = ModelRegistry.DeepSeekChat.Id;

    /// <summary>
    /// Whether to allow users to override model selection
    /// </summary>
    public bool AllowUserOverride { get; set; } = true;

    /// <summary>
    /// Model mappings for different use cases and PerkLevels
    /// </summary>
    public List<SnChanModelMapping> Mappings { get; set; } = new()
    {
        // Default mappings - SN-chan Chat
        new SnChanModelMapping
        {
            UseCase = ModelUseCase.SnChanChat,
            ModelId = ModelRegistry.DeepSeekChat.Id,
            MinPerkLevel = 0,
            IsDefault = true,
            DisplayName = "DeepSeek Chat",
            Description = "Fast and efficient for everyday conversations"
        },
        new SnChanModelMapping
        {
            UseCase = ModelUseCase.SnChanChat,
            ModelId = ModelRegistry.DeepSeekReasoner.Id,
            MinPerkLevel = 1,
            DisplayName = "DeepSeek Reasoner",
            Description = "Advanced reasoning for complex discussions"
        },

        // SN-chan Reasoning
        new SnChanModelMapping
        {
            UseCase = ModelUseCase.SnChanReasoning,
            ModelId = ModelRegistry.DeepSeekReasoner.Id,
            MinPerkLevel = 0,
            IsDefault = true,
            DisplayName = "DeepSeek Reasoner",
            Description = "Optimized for complex reasoning tasks"
        },

        // SN-chan Vision
        new SnChanModelMapping
        {
            UseCase = ModelUseCase.SnChanVision,
            ModelId = ModelRegistry.QwenVision.Id,
            MinPerkLevel = 0,
            IsDefault = true,
            DisplayName = "Qwen Vision",
            Description = "Vision analysis with Qwen"
        },
        new SnChanModelMapping
        {
            UseCase = ModelUseCase.SnChanVision,
            ModelId = ModelRegistry.ClaudeOpus.Id,
            MinPerkLevel = 2,
            DisplayName = "Claude 3 Opus",
            Description = "Premium vision analysis with Claude"
        }
    };

    /// <summary>
    /// Gets available models for a use case and PerkLevel
    /// </summary>
    public IEnumerable<SnChanModelMapping> GetAvailableModels(ModelUseCase useCase, int perkLevel)
    {
        return Mappings
            .Where(m => m.UseCase == useCase && m.Enabled && m.MinPerkLevel <= perkLevel)
            .OrderByDescending(m => m.Priority)
            .ThenBy(m => m.MinPerkLevel);
    }

    /// <summary>
    /// Gets the default model for a use case
    /// </summary>
    public SnChanModelMapping? GetDefaultMapping(ModelUseCase useCase, int perkLevel)
    {
        var available = GetAvailableModels(useCase, perkLevel);
        return available.FirstOrDefault(m => m.IsDefault) ?? available.FirstOrDefault();
    }
}

/// <summary>
/// Maps a model to a use case with PerkLevel requirements for SN-chan
/// </summary>
public class SnChanModelMapping
{
    /// <summary>
    /// The use case this mapping applies to
    /// </summary>
    public ModelUseCase UseCase { get; set; }

    /// <summary>
    /// The model ID from Thinking:Services or ModelRegistry
    /// </summary>
    public string ModelId { get; set; } = "";

    /// <summary>
    /// Minimum PerkLevel required to use this model
    /// </summary>
    public int MinPerkLevel { get; set; } = 0;

    /// <summary>
    /// Maximum PerkLevel allowed (null = no limit)
    /// </summary>
    public int? MaxPerkLevel { get; set; }

    /// <summary>
    /// Whether this is the default model for this use case
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Priority for model selection (higher = preferred)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Display name for UI
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description for UI/help
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this mapping is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

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
}
