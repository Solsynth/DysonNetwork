namespace DysonNetwork.Insight.Agent.Models;

/// <summary>
/// Context for model selection, containing user and request information
/// </summary>
public class UserModelContext
{
    /// <summary>
    /// The user's account ID
    /// </summary>
    public Guid? AccountId { get; set; }

    /// <summary>
    /// The user's current PerkLevel
    /// </summary>
    public int PerkLevel { get; set; } = 0;

    /// <summary>
    /// Whether the user is a superuser (bypasses PerkLevel checks)
    /// </summary>
    public bool IsSuperuser { get; set; } = false;

    /// <summary>
    /// The requested use case
    /// </summary>
    public ModelUseCase UseCase { get; set; } = ModelUseCase.Default;

    /// <summary>
    /// User's preferred model ID (if any)
    /// </summary>
    public string? PreferredModelId { get; set; }

    /// <summary>
    /// Whether to use the best available model for the user's PerkLevel
    /// </summary>
    public bool UseBestAvailable { get; set; } = true;

    /// <summary>
    /// Optional temperature override
    /// </summary>
    public double? Temperature { get; set; }

    /// <summary>
    /// Optional reasoning effort override
    /// </summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// Creates a context for a specific use case with user's PerkLevel
    /// </summary>
    public static UserModelContext ForUseCase(ModelUseCase useCase, int perkLevel = 0, bool isSuperuser = false) =>
        new() { UseCase = useCase, PerkLevel = perkLevel, IsSuperuser = isSuperuser };

    /// <summary>
    /// Creates a context for MiChan chat
    /// </summary>
    public static UserModelContext ForMiChan(int perkLevel = 0, bool isSuperuser = false) =>
        new() { UseCase = ModelUseCase.MiChanChat, PerkLevel = perkLevel, IsSuperuser = isSuperuser };

    /// <summary>
    /// Creates a context for SN-chan chat
    /// </summary>
    public static UserModelContext ForSnChan(int perkLevel = 0, bool isSuperuser = false) =>
        new() { UseCase = ModelUseCase.SnChanChat, PerkLevel = perkLevel, IsSuperuser = isSuperuser };
}

/// <summary>
/// Result of a model selection operation
/// </summary>
public class ModelSelectionResult
{
    /// <summary>
    /// Whether a suitable model was found
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The selected model configuration
    /// </summary>
    public ModelConfiguration? Configuration { get; set; }

    /// <summary>
    /// The mapping that was selected
    /// </summary>
    public ModelUseCaseMapping? Mapping { get; set; }

    /// <summary>
    /// Error message if selection failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the user's preferred model was used
    /// </summary>
    public bool UsedPreferredModel { get; set; }

    /// <summary>
    /// Whether the default model was used (fallback)
    /// </summary>
    public bool UsedFallback { get; set; }

    /// <summary>
    /// Available models for the user's PerkLevel (for UI display)
    /// </summary>
    public List<ModelUseCaseMapping> AvailableModels { get; set; } = new();

    public static ModelSelectionResult Successful(
        ModelConfiguration config,
        ModelUseCaseMapping mapping,
        List<ModelUseCaseMapping> available,
        bool usedPreferred = false) => new()
    {
        Success = true,
        Configuration = config,
        Mapping = mapping,
        AvailableModels = available,
        UsedPreferredModel = usedPreferred
    };

    public static ModelSelectionResult Failed(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Service for selecting the appropriate AI model based on use case and user context
/// </summary>
public interface IModelSelector
{
    /// <summary>
    /// Selects the best model for the given context
    /// </summary>
    ModelSelectionResult SelectModel(UserModelContext context);

    /// <summary>
    /// Selects a model for a specific use case with PerkLevel
    /// </summary>
    ModelSelectionResult SelectModel(ModelUseCase useCase, int perkLevel = 0, string? preferredModelId = null);

    /// <summary>
    /// Gets all available models for a use case and PerkLevel (for UI display)
    /// </summary>
    IEnumerable<ModelUseCaseMapping> GetAvailableModels(ModelUseCase useCase, int perkLevel);

    /// <summary>
    /// Checks if a user can access a specific model for a use case
    /// </summary>
    bool CanAccessModel(ModelUseCase useCase, string modelId, int perkLevel);

    /// <summary>
    /// Gets the default model configuration for a use case
    /// </summary>
    ModelConfiguration GetDefaultConfiguration(ModelUseCase useCase, int perkLevel = 0);
}
