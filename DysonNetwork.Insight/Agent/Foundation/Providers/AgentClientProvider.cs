namespace DysonNetwork.Insight.Agent.Foundation.Providers;

using DysonNetwork.Insight.Agent.Models;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Shared.Registry;

/// <summary>
/// Provides ChatClient instances for different use cases.
/// Replaces MiChanKernelProvider with direct OpenAI SDK usage.
/// </summary>
public interface IAgentClientProvider
{
    string GetProviderId(int? userPerkLevel = null, string? preferredModelId = null);
    string GetAutonomousProviderId(int? userPerkLevel = null);
    string GetVisionProviderId(int? userPerkLevel = null);
    string GetCompactionProviderId(int? userPerkLevel = null);
    string GetScheduledTaskProviderId(int? userPerkLevel = null);
    string GetTopicGenerationProviderId(int? userPerkLevel = null);
    AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null);
    string GetServiceId();
    IEnumerable<MiChanModelMapping> GetAvailableModelsForUseCase(ModelUseCase useCase, int userPerkLevel);
    bool IsVisionModelAvailable();
}

/// <summary>
/// Marker interface for MiChan client provider
/// </summary>
public interface IMiChanClientProvider : IAgentClientProvider
{
}

/// <summary>
/// Marker interface for SnChan client provider
/// </summary>
public interface ISnChanClientProvider : IAgentClientProvider
{
}

/// <summary>
/// Provider for MiChan ChatClient configuration
/// </summary>
public class MiChanClientProvider : IMiChanClientProvider
{
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanClientProvider> _logger;
    private readonly ModelRegistry _modelRegistry;

    public MiChanClientProvider(MiChanConfig config, ILogger<MiChanClientProvider> logger, ModelRegistry modelRegistry)
    {
        _config = config;
        _logger = logger;
        _modelRegistry = modelRegistry;
    }

    public string GetProviderId(int? userPerkLevel = null, string? preferredModelId = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanChat, userPerkLevel, preferredModelId);
        return GetServiceIdFromModel(modelConfig);
    }

    public string GetAutonomousProviderId(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanAutonomous, userPerkLevel);
        return GetServiceIdFromModel(modelConfig);
    }

    public string GetVisionProviderId(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanVision, userPerkLevel);
        return GetServiceIdFromModel(modelConfig);
    }

    public string GetCompactionProviderId(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanCompaction, userPerkLevel);
        return GetServiceIdFromModel(modelConfig);
    }

    public string GetScheduledTaskProviderId(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanScheduledTask, userPerkLevel);
        return GetServiceIdFromModel(modelConfig);
    }

    public string GetTopicGenerationProviderId(int? userPerkLevel = null)
    {
        var modelConfig = SelectModelForUseCase(ModelUseCase.MiChanTopicGeneration, userPerkLevel);
        return GetServiceIdFromModel(modelConfig);
    }

    public AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null)
    {
        return new AgentExecutionOptions
        {
            Temperature = (float)(temperature ?? _config.ThinkingModel.GetEffectiveTemperature(_modelRegistry)),
            ReasoningEffort = reasoningEffort ?? _config.ThinkingModel.GetEffectiveReasoningEffort(_modelRegistry)
        };
    }

    /// <summary>
    /// Converts a ModelConfiguration to a service ID for the provider registry
    /// </summary>
    private string GetServiceIdFromModel(ModelConfiguration modelConfig)
    {
        // The service ID is the model ID from configuration
        // The AgentChatClientFactory will look up the full config from Thinking:Services
        return modelConfig.ModelId;
    }

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
    public IEnumerable<ModelRef> GetAvailableModels() => _modelRegistry.All;

    /// <summary>
    /// Gets models that support vision
    /// </summary>
    public IEnumerable<ModelRef> GetVisionModels() => _modelRegistry.VisionModels;

    /// <summary>
    /// Gets models that support reasoning
    /// </summary>
    public IEnumerable<ModelRef> GetReasoningModels() => _modelRegistry.ReasoningModels;

    /// <summary>
    /// Gets available models for a specific use case based on PerkLevel
    /// </summary>
    public IEnumerable<MiChanModelMapping> GetAvailableModelsForUseCase(ModelUseCase useCase, int userPerkLevel)
    {
        if (_config.UseModelSelection)
        {
            return _config.ModelSelection.GetAvailableModels(useCase, userPerkLevel);
        }

        return _modelRegistry.All.Select(m => new MiChanModelMapping
        {
            UseCase = useCase,
            ModelId = m.Id,
            MinPerkLevel = 0,
            DisplayName = m.DisplayName,
            Enabled = true
        });
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

        var selection = _config.ModelSelection;

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

        var defaultMapping = selection.GetDefaultMapping(useCase, perkLevel);
        if (defaultMapping != null)
        {
            return new ModelConfiguration { ModelId = defaultMapping.ModelId };
        }

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

    public bool IsVisionModelAvailable()
    {
        var modelRef = _config.GetVisionModel().GetModelRef(_modelRegistry);
        if (modelRef == null) return false;
        return !string.IsNullOrEmpty(modelRef.ModelName);
    }
}

/// <summary>
/// Provider for SnChan (Thought) ChatClient configuration
/// </summary>
public class SnChanClientProvider : ISnChanClientProvider
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SnChanClientProvider> _logger;
    private readonly ModelConfiguration _defaultModel;
    private readonly ModelRegistry _modelRegistry;

    public SnChanClientProvider(IConfiguration configuration, ILogger<SnChanClientProvider> logger, ModelRegistry modelRegistry)
    {
        _configuration = configuration;
        _logger = logger;
        _modelRegistry = modelRegistry;

        var cfg = configuration.GetSection("Thinking");
        var defaultServiceId = cfg.GetValue<string>("DefaultService") ?? "deepseek-chat";

        _defaultModel = new ModelConfiguration
        {
            ModelId = defaultServiceId,
            Temperature = cfg.GetValue<double?>("DefaultTemperature") ?? 0.7,
            EnableFunctions = true
        };
    }

    public string GetProviderId(int? userPerkLevel = null, string? preferredModelId = null)
    {
        return preferredModelId ?? _defaultModel.ModelId;
    }

    public string GetAutonomousProviderId(int? userPerkLevel = null) => GetProviderId(userPerkLevel);
    public string GetVisionProviderId(int? userPerkLevel = null) => GetProviderId(userPerkLevel);
    public string GetCompactionProviderId(int? userPerkLevel = null) => GetProviderId(userPerkLevel);
    public string GetScheduledTaskProviderId(int? userPerkLevel = null) => GetProviderId(userPerkLevel);
    public string GetTopicGenerationProviderId(int? userPerkLevel = null) => GetProviderId(userPerkLevel);

    public string GetServiceId() => _defaultModel.ModelId;

    public AgentExecutionOptions CreateExecutionOptions(double? temperature = null, string? reasoningEffort = null)
    {
        return new AgentExecutionOptions
        {
            Temperature = (float)(temperature ?? _defaultModel.GetEffectiveTemperature(_modelRegistry)),
            ReasoningEffort = reasoningEffort ?? _defaultModel.GetEffectiveReasoningEffort(_modelRegistry)
        };
    }

    public string GetDefaultProviderId() => _defaultModel.ModelId;

    public ModelConfiguration GetDefaultModel() => _defaultModel;

    public IEnumerable<MiChanModelMapping> GetAvailableModelsForUseCase(ModelUseCase useCase, int userPerkLevel)
    {
        return _modelRegistry.All.Select(m => new MiChanModelMapping
        {
            UseCase = useCase,
            ModelId = m.Id,
            MinPerkLevel = 0,
            DisplayName = m.DisplayName,
            Enabled = true
        });
    }

    public bool IsVisionModelAvailable()
    {
        if (!_configuration.GetValue<bool?>("SnChan:EnableVision").GetValueOrDefault(true))
        {
            return false;
        }

        var serviceId = _configuration.GetValue<string>("SnChan:VisionModel:ModelId")
                        ?? _configuration.GetValue<string>("SnChan:DefaultChatModel:ModelId")
                        ?? _configuration.GetValue<string>("Thinking:DefaultService")
                        ?? "deepseek-chat";
        var modelRef = _modelRegistry.GetById(serviceId);
        return modelRef != null && !string.IsNullOrEmpty(modelRef.ModelName);
    }
}
