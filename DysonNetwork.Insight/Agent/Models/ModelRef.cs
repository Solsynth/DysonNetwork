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
/// Predefined model references for common models.
/// Add new models here as they become available.
/// </summary>
public static class ModelRegistry
{
    // DeepSeek Models
    public static readonly ModelRef DeepSeekChat = new(
        id: "deepseek-chat",
        provider: "deepseek",
        modelName: "deepseek-chat",
        displayName: "DeepSeek Chat",
        supportsReasoning: false,
        defaultTemperature: 0.75);

    public static readonly ModelRef DeepSeekReasoner = new(
        id: "deepseek-reasoner",
        provider: "deepseek",
        modelName: "deepseek-reasoner",
        displayName: "DeepSeek Reasoner",
        supportsReasoning: true,
        defaultTemperature: 0.7,
        defaultReasoningEffort: "high");

    // OpenRouter Models
    public static readonly ModelRef ClaudeOpus = new(
        id: "vision-openrouter",
        provider: "openrouter",
        modelName: "anthropic/claude-3-opus",
        displayName: "Claude 3 Opus",
        supportsVision: true,
        supportsReasoning: true,
        defaultTemperature: 0.7);

    public static readonly ModelRef ClaudeSonnet = new(
        id: "claude-sonnet",
        provider: "openrouter",
        modelName: "anthropic/claude-3-sonnet",
        displayName: "Claude 3 Sonnet",
        supportsVision: true,
        defaultTemperature: 0.7);

    public static readonly ModelRef GPT4 = new(
        id: "gpt-4",
        provider: "openrouter",
        modelName: "openai/gpt-4",
        displayName: "GPT-4",
        supportsVision: true,
        defaultTemperature: 0.7);

    public static readonly ModelRef GPT4Turbo = new(
        id: "gpt-4-turbo",
        provider: "openrouter",
        modelName: "openai/gpt-4-turbo",
        displayName: "GPT-4 Turbo",
        supportsVision: true,
        defaultTemperature: 0.7);

    // Aliyun Models
    public static readonly ModelRef QwenMax = new(
        id: "qwen-max",
        provider: "aliyun",
        modelName: "qwen-max",
        displayName: "Qwen Max",
        supportsVision: true,
        defaultTemperature: 0.7);

    public static readonly ModelRef QwenVision = new(
        id: "vision-aliyun",
        provider: "aliyun",
        modelName: "qwen-vl-max",
        displayName: "Qwen VL Max",
        supportsVision: true,
        defaultTemperature: 0.7);

    // Ollama Models (local)
    public static readonly ModelRef LlamaLocal = new(
        id: "ollama-llama",
        provider: "ollama",
        modelName: "llama3.1",
        displayName: "Llama 3.1 (Local)",
        defaultTemperature: 0.7);

    private static readonly Dictionary<string, ModelRef> _models = new()
    {
        [DeepSeekChat.Id] = DeepSeekChat,
        [DeepSeekReasoner.Id] = DeepSeekReasoner,
        [ClaudeOpus.Id] = ClaudeOpus,
        [ClaudeSonnet.Id] = ClaudeSonnet,
        [GPT4.Id] = GPT4,
        [GPT4Turbo.Id] = GPT4Turbo,
        [QwenMax.Id] = QwenMax,
        [QwenVision.Id] = QwenVision,
        [LlamaLocal.Id] = LlamaLocal,
    };

    /// <summary>
    /// Gets all registered models
    /// </summary>
    public static IEnumerable<ModelRef> All => _models.Values;

    /// <summary>
    /// Gets all models that support vision
    /// </summary>
    public static IEnumerable<ModelRef> VisionModels => _models.Values.Where(m => m.SupportsVision);

    /// <summary>
    /// Gets all models that support reasoning
    /// </summary>
    public static IEnumerable<ModelRef> ReasoningModels => _models.Values.Where(m => m.SupportsReasoning);

    /// <summary>
    /// Gets a model by its ID
    /// </summary>
    public static ModelRef? GetById(string id) =>
        _models.TryGetValue(id, out var model) ? model : null;

    /// <summary>
    /// Gets a model by ID or returns a default if not found
    /// </summary>
    public static ModelRef GetByIdOrDefault(string id, ModelRef defaultModel) =>
        GetById(id) ?? defaultModel;

    /// <summary>
    /// Tries to get a model by ID
    /// </summary>
    public static bool TryGetById(string id, out ModelRef model) =>
        _models.TryGetValue(id, out model!);

    /// <summary>
    /// Registers a custom model at runtime
    /// </summary>
    public static void Register(ModelRef model)
    {
        _models[model.Id] = model;
    }

    /// <summary>
    /// Validates that a model ID exists in the registry
    /// </summary>
    public static bool IsValid(string id) => _models.ContainsKey(id);
}
