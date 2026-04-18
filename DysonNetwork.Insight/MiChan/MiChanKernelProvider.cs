#pragma warning disable SKEXP0050

using System.Diagnostics.CodeAnalysis;
using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan.KernelBuilding;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Provides Semantic Kernel instances for MiChan with fluent configuration.
/// Uses IKernelBuilder for consistent, maintainable kernel construction.
/// Supports PerkLevel-based model selection for different use cases.
/// </summary>
public class MiChanKernelProvider
{
    private readonly MiChanConfig _config;
    private readonly Agent.KernelBuilding.IKernelBuilder _kernelBuilder;
    private readonly IServiceProvider _serviceProvider;

    public MiChanKernelProvider(
        MiChanConfig config,
        Agent.KernelBuilding.IKernelBuilder kernelBuilder,
        IServiceProvider serviceProvider)
    {
        _config = config;
        _kernelBuilder = kernelBuilder;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the primary kernel for chat conversations
    /// </summary>
    [Experimental("SKEXP0050")]
    public Kernel GetKernel(int? userPerkLevel = null, string? preferredModelId = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanChat, userPerkLevel, preferredModelId);

        return _kernelBuilder
            .WithModel(modelConfig)
            .ForMiChanChat(_config, _serviceProvider)
            .Build();
    }

    /// <summary>
    /// Gets the kernel for autonomous behavior
    /// </summary>
    [Experimental("SKEXP0050")]
    public Kernel GetAutonomousKernel(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanAutonomous, userPerkLevel);

        return _kernelBuilder
            .WithModel(modelConfig)
            .ForMiChanAutonomous(_config, _serviceProvider)
            .Build();
    }

    /// <summary>
    /// Gets the kernel for vision analysis
    /// </summary>
    [Experimental("SKEXP0050")]
    public Kernel GetVisionKernel(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanVision, userPerkLevel);

        return _kernelBuilder
            .WithModel(modelConfig)
            .ForMiChanVision(_config)
            .Build();
    }

    /// <summary>
    /// Gets the kernel for scheduled tasks
    /// </summary>
    [Experimental("SKEXP0050")]
    public Kernel GetScheduledTaskKernel(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanScheduledTask, userPerkLevel);

        return _kernelBuilder
            .WithModel(modelConfig)
            .WithEmbeddings(true)
            .WithWebSearch(true)
            .WithAutoInvoke(true)
            .WithMiChanPlugins(_serviceProvider)
            .WithServiceProvider(_serviceProvider)
            .Build();
    }

    /// <summary>
    /// Gets the kernel for topic generation
    /// </summary>
    [Experimental("SKEXP0050")]
    public Kernel GetTopicGenerationKernel(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanTopicGeneration, userPerkLevel);

        return _kernelBuilder
            .WithModel(modelConfig)
            .ForTopicGeneration(_config)
            .Build();
    }

    /// <summary>
    /// Gets the kernel for conversation compaction
    /// </summary>
    [Experimental("SKEXP0050")]
    public Kernel GetCompactionKernel(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanCompaction, userPerkLevel);

        return _kernelBuilder
            .WithModel(modelConfig)
            .ForCompaction(_config)
            .Build();
    }

    /// <summary>
    /// Creates a kernel with a specific model configuration
    /// </summary>
    [Experimental("SKEXP0050")]
    public Kernel GetKernelForModel(ModelConfiguration modelConfig)
    {
        return _kernelBuilder
            .WithModel(modelConfig)
            .WithEmbeddings(true)
            .WithWebSearch(true)
            .WithMiChanPlugins(_serviceProvider)
            .WithServiceProvider(_serviceProvider)
            .Build();
    }

    /// <summary>
    /// Creates prompt execution settings for the primary model
    /// </summary>
    public PromptExecutionSettings CreatePromptExecutionSettings(double? temperature = null, string? reasoningEffort = null)
    {
        return _kernelBuilder
            .WithModel(_config.ThinkingModel)
            .WithTemperature(temperature ?? _config.ThinkingModel.GetEffectiveTemperature())
            .WithReasoningEffort(reasoningEffort ?? _config.ThinkingModel.GetEffectiveReasoningEffort()!)
            .CreatePromptExecutionSettings();
    }

    /// <summary>
    /// Creates prompt execution settings for autonomous behavior
    /// </summary>
    public PromptExecutionSettings CreateAutonomousPromptExecutionSettings(double? temperature = null, string? reasoningEffort = null)
    {
        var model = _config.GetAutonomousModel();
        var defaultEffort = model.ModelId != _config.ThinkingModel.ModelId ? "high" : null;

        return _kernelBuilder
            .WithModel(model)
            .WithTemperature(temperature ?? model.GetEffectiveTemperature())
            .WithReasoningEffort(reasoningEffort ?? defaultEffort ?? model.GetEffectiveReasoningEffort()!)
            .CreatePromptExecutionSettings();
    }

    /// <summary>
    /// Creates prompt execution settings for vision analysis
    /// </summary>
    public PromptExecutionSettings CreateVisionPromptExecutionSettings(double? temperature = null, string? reasoningEffort = null)
    {
        var visionModel = _config.GetVisionModel();

        return _kernelBuilder
            .WithModel(visionModel)
            .WithTemperature(temperature ?? visionModel.GetEffectiveTemperature())
            .WithReasoningEffort(reasoningEffort ?? visionModel.GetEffectiveReasoningEffort()!)
            .CreatePromptExecutionSettings();
    }

    /// <summary>
    /// Creates prompt execution settings for scheduled tasks
    /// </summary>
    public PromptExecutionSettings CreateScheduledTaskPromptExecutionSettings(double? temperature = null)
    {
        var model = _config.GetScheduledTaskModel();

        return _kernelBuilder
            .WithModel(model)
            .WithTemperature(temperature ?? model.GetEffectiveTemperature())
            .WithAutoInvoke(true)
            .CreatePromptExecutionSettings();
    }

    /// <summary>
    /// Creates prompt execution settings for a specific model configuration
    /// </summary>
    public PromptExecutionSettings CreatePromptExecutionSettingsForModel(
        ModelConfiguration modelConfig,
        double? temperature = null,
        string? reasoningEffort = null)
    {
        return _kernelBuilder
            .WithModel(modelConfig)
            .WithTemperature(temperature ?? modelConfig.GetEffectiveTemperature())
            .WithReasoningEffort(reasoningEffort ?? modelConfig.GetEffectiveReasoningEffort()!)
            .CreatePromptExecutionSettings();
    }

    /// <summary>
    /// Creates prompt execution settings for a specific use case with PerkLevel-based selection
    /// </summary>
    public PromptExecutionSettings CreatePromptExecutionSettingsForUseCase(
        ModelUseCase useCase,
        int? userPerkLevel = null,
        double? temperature = null,
        string? reasoningEffort = null)
    {
        var modelConfig = SelectModelForUseCase(useCase, userPerkLevel);

        return _kernelBuilder
            .WithModel(modelConfig)
            .WithTemperature(temperature ?? modelConfig.GetEffectiveTemperature())
            .WithReasoningEffort(reasoningEffort ?? modelConfig.GetEffectiveReasoningEffort()!)
            .CreatePromptExecutionSettings();
    }

    /// <summary>
    /// Checks if the vision model is available
    /// </summary>
    public bool IsVisionModelAvailable()
    {
        var modelRef = _config.GetVisionModel().GetModelRef();
        if (modelRef == null) return false;

        // Check using the kernel builder's underlying factory
        // This is a simplified check - in production you might want to actually test the connection
        return !string.IsNullOrEmpty(modelRef.ModelName);
    }

    /// <summary>
    /// Gets the gateway URL from configuration
    /// </summary>
    public string GetGatewayUrl() => _config.GatewayUrl;

    /// <summary>
    /// Gets the access token from configuration
    /// </summary>
    public string GetAccessToken() => _config.AccessToken;

    /// <summary>
    /// Gets the primary service ID (model ID)
    /// </summary>
    public string GetServiceId() => _config.ThinkingModel.ModelId;

    /// <summary>
    /// Gets the primary model configuration
    /// </summary>
    public ModelConfiguration GetThinkingModel() => _config.ThinkingModel;

    /// <summary>
    /// Gets the autonomous model configuration
    /// </summary>
    public ModelConfiguration GetAutonomousModel() => _config.GetAutonomousModel();

    /// <summary>
    /// Gets the vision model configuration
    /// </summary>
    public ModelConfiguration GetVisionModel() => _config.GetVisionModel();

    /// <summary>
    /// Gets all available models from the registry
    /// </summary>
    public IEnumerable<ModelRef> GetAvailableModels() => ModelRegistry.All;

    /// <summary>
    /// Gets models that support vision
    /// </summary>
    public IEnumerable<ModelRef> GetVisionModels() => ModelRegistry.VisionModels;

    /// <summary>
    /// Gets models that support reasoning
    /// </summary>
    public IEnumerable<ModelRef> GetReasoningModels() => ModelRegistry.ReasoningModels;

    /// <summary>
    /// Gets available models for a specific use case based on PerkLevel
    /// </summary>
    public IEnumerable<MiChanModelMapping> GetAvailableModelsForUseCase(ModelUseCase useCase, int userPerkLevel)
    {
        if (_config.UseModelSelection)
        {
            return _config.ModelSelection.GetAvailableModels(useCase, userPerkLevel);
        }

        // Fallback to legacy behavior - return all registered models
        return ModelRegistry.All.Select(m => new MiChanModelMapping
        {
            UseCase = useCase,
            ModelId = m.Id,
            MinPerkLevel = 0,
            DisplayName = m.DisplayName,
            Enabled = true
        });
    }

    /// <summary>
    /// Switches the primary model at runtime (if allowed)
    /// </summary>
    public bool TrySwitchModel(string modelId)
    {
        var model = ModelRegistry.GetById(modelId);
        if (model == null) return false;

        if (!_config.ThinkingModel.AllowRuntimeSwitch)
            return false;

        _config.ThinkingModel.ModelId = modelId;
        return true;
    }

    /// <summary>
    /// Switches the autonomous model at runtime (if allowed)
    /// </summary>
    public bool TrySwitchAutonomousModel(string modelId)
    {
        var model = ModelRegistry.GetById(modelId);
        if (model == null) return false;

        var targetConfig = _config.AutonomousModel ?? _config.ThinkingModel;
        if (!targetConfig.AllowRuntimeSwitch)
            return false;

        if (_config.AutonomousModel == null)
        {
            _config.AutonomousModel = new ModelConfiguration { ModelId = modelId };
        }
        else
        {
            _config.AutonomousModel.ModelId = modelId;
        }

        return true;
    }

    /// <summary>
    /// Selects the appropriate model for a use case based on PerkLevel
    /// </summary>
    private ModelConfiguration SelectModelForUseCase(
        ModelUseCase useCase,
        int? userPerkLevel = null,
        string? preferredModelId = null)
    {
        var perkLevel = userPerkLevel ?? 0;

        // If model selection is not enabled, use legacy behavior
        if (!_config.UseModelSelection)
        {
            return useCase switch
            {
                ModelUseCase.MiChanAutonomous => _config.GetAutonomousModel(),
                ModelUseCase.MiChanVision => _config.GetVisionModel(),
                ModelUseCase.MiChanScheduledTask => _config.GetScheduledTaskModel(),
                ModelUseCase.MiChanCompaction => _config.GetCompactionModel(),
                ModelUseCase.MiChanTopicGeneration => _config.GetTopicGenerationModel(),
                _ => _config.ThinkingModel
            };
        }

        // Use new model selection system
        var selection = _config.ModelSelection;

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

        // Fallback to legacy configuration
        return useCase switch
        {
            ModelUseCase.MiChanAutonomous => _config.GetAutonomousModel(),
            ModelUseCase.MiChanVision => _config.GetVisionModel(),
            ModelUseCase.MiChanScheduledTask => _config.GetScheduledTaskModel(),
            ModelUseCase.MiChanCompaction => _config.GetCompactionModel(),
            ModelUseCase.MiChanTopicGeneration => _config.GetTopicGenerationModel(),
            _ => _config.ThinkingModel
        };
    }
}

#pragma warning restore SKEXP0050
