namespace DysonNetwork.Insight.Agent.Models;

/// <summary>
/// Strongly-typed reference to an AI model configuration.
/// Eliminates magic strings and provides compile-time safety for model selection.
/// Supports custom providers with custom base URLs for OpenAI-compatible APIs.
/// </summary>
public sealed record ModelRef
{
    /// <summary>
    /// The configuration key used in appsettings (e.g., "deepseek-chat")
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The provider type (e.g., "deepseek", "openrouter", "aliyun", "custom")
    /// </summary>
    public string Provider { get; }

    /// <summary>
    /// The actual model name (e.g., "deepseek-chat", "anthropic/claude-3-opus")
    /// </summary>
    public string ModelName { get; }

    /// <summary>
    /// Human-readable display name
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Whether this model supports vision/image analysis
    /// </summary>
    public bool SupportsVision { get; }

    /// <summary>
    /// Whether this model supports reasoning/thinking
    /// </summary>
    public bool SupportsReasoning { get; }

    /// <summary>
    /// Default temperature for this model
    /// </summary>
    public double DefaultTemperature { get; }

    /// <summary>
    /// Default reasoning effort (low/medium/high) if supported
    /// </summary>
    public string? DefaultReasoningEffort { get; }

    /// <summary>
    /// Custom base URL for the API endpoint (for custom providers)
    /// </summary>
    public string? BaseUrl { get; }

    /// <summary>
    /// API key for the provider (for custom providers, null means use config)
    /// </summary>
    public string? ApiKey { get; }

    /// <summary>
    /// Whether this is a custom provider (not a built-in provider)
    /// </summary>
    public bool IsCustomProvider => Provider == "custom" || !string.IsNullOrEmpty(BaseUrl);

    public ModelRef(
        string id,
        string provider,
        string modelName,
        string? displayName = null,
        bool supportsVision = false,
        bool supportsReasoning = false,
        double defaultTemperature = 0.7,
        string? defaultReasoningEffort = null,
        string? baseUrl = null,
        string? apiKey = null)
    {
        Id = id;
        Provider = provider;
        ModelName = modelName;
        DisplayName = displayName ?? id;
        SupportsVision = supportsVision;
        SupportsReasoning = supportsReasoning;
        DefaultTemperature = defaultTemperature;
        DefaultReasoningEffort = defaultReasoningEffort;
        BaseUrl = baseUrl;
        ApiKey = apiKey;
    }

    /// <summary>
    /// Creates a copy of this ModelRef with a custom base URL
    /// </summary>
    public ModelRef WithBaseUrl(string baseUrl) =>
        new(Id, Provider, ModelName, DisplayName, SupportsVision, SupportsReasoning,
            DefaultTemperature, DefaultReasoningEffort, baseUrl, ApiKey);

    /// <summary>
    /// Creates a copy of this ModelRef with a custom API key
    /// </summary>
    public ModelRef WithApiKey(string apiKey) =>
        new(Id, Provider, ModelName, DisplayName, SupportsVision, SupportsReasoning,
            DefaultTemperature, DefaultReasoningEffort, BaseUrl, apiKey);

    /// <summary>
    /// Creates a custom provider model reference
    /// </summary>
    public static ModelRef CreateCustom(
        string id,
        string modelName,
        string baseUrl,
        string? apiKey = null,
        string? displayName = null,
        bool supportsVision = false,
        bool supportsReasoning = false,
        double defaultTemperature = 0.7) =>
        new(id, "custom", modelName, displayName ?? id, supportsVision, supportsReasoning,
            defaultTemperature, null, baseUrl, apiKey);

    public override string ToString() => Id;

    /// <summary>
    /// Implicit conversion to string for backward compatibility
    /// </summary>
    public static implicit operator string(ModelRef modelRef) => modelRef.Id;
}

/// <summary>
/// Registry of AI model references built from configuration.
/// Initialized from appsettings.json Thinking:Services section.
/// </summary>
public class ModelRegistry
{
    private readonly Dictionary<string, ModelRef> _models = new();

    public ModelRegistry(ThinkingConfig config)
    {
        InitializeFromConfig(config);
    }

    private void InitializeFromConfig(ThinkingConfig config)
    {
        foreach (var (serviceId, serviceConfig) in config.Services)
        {
            var modelRef = new ModelRef(
                id: serviceId,
                provider: serviceConfig.Provider,
                modelName: serviceConfig.Model,
                displayName: serviceConfig.DisplayName ?? serviceId,
                supportsVision: serviceConfig.SupportsVision,
                supportsReasoning: serviceConfig.SupportsReasoning,
                defaultTemperature: serviceConfig.Temperature ?? 0.7,
                defaultReasoningEffort: serviceConfig.ReasoningEffort,
                baseUrl: serviceConfig.Endpoint,
                apiKey: serviceConfig.ApiKey);

            _models[serviceId] = modelRef;
        }
    }

    /// <summary>
    /// Gets all registered models
    /// </summary>
    public IEnumerable<ModelRef> All => _models.Values;

    /// <summary>
    /// Gets all models that support vision
    /// </summary>
    public IEnumerable<ModelRef> VisionModels => _models.Values.Where(m => m.SupportsVision);

    /// <summary>
    /// Gets all models that support reasoning
    /// </summary>
    public IEnumerable<ModelRef> ReasoningModels => _models.Values.Where(m => m.SupportsReasoning);

    /// <summary>
    /// Gets a model by its ID
    /// </summary>
    public ModelRef? GetById(string id) =>
        _models.TryGetValue(id, out var model) ? model : null;

    /// <summary>
    /// Gets a model by ID or returns a default if not found
    /// </summary>
    public ModelRef GetByIdOrDefault(string id, ModelRef defaultModel) =>
        GetById(id) ?? defaultModel;

    /// <summary>
    /// Tries to get a model by ID
    /// </summary>
    public bool TryGetById(string id, out ModelRef model) =>
        _models.TryGetValue(id, out model!);

    /// <summary>
    /// Registers a custom model at runtime
    /// </summary>
    public void Register(ModelRef model)
    {
        _models[model.Id] = model;
    }

    /// <summary>
    /// Validates that a model ID exists in the registry
    /// </summary>
    public bool IsValid(string id) => _models.ContainsKey(id);
}
