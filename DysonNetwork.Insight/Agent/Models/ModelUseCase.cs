namespace DysonNetwork.Insight.Agent.Models;

/// <summary>
/// Defines the use cases for AI model selection.
/// Each use case can have different model configurations with PerkLevel requirements.
/// </summary>
public enum ModelUseCase
{
    /// <summary>
    /// Default/unspecified use case
    /// </summary>
    Default = 0,

    /// <summary>
    /// MiChan chat conversations - standard interactive chat
    /// </summary>
    MiChanChat = 1,

    /// <summary>
    /// MiChan autonomous behavior - self-directed actions
    /// </summary>
    MiChanAutonomous = 2,

    /// <summary>
    /// MiChan vision analysis - image understanding
    /// </summary>
    MiChanVision = 3,

    /// <summary>
    /// MiChan scheduled tasks - periodic background tasks
    /// </summary>
    MiChanScheduledTask = 4,

    /// <summary>
    /// MiChan conversation compaction - summarizing long conversations
    /// </summary>
    MiChanCompaction = 5,

    /// <summary>
    /// MiChan topic generation - generating conversation topics
    /// </summary>
    MiChanTopicGeneration = 6,

    /// <summary>
    /// SN-chan chat - user-facing chat interface
    /// </summary>
    SnChanChat = 10,

    /// <summary>
    /// SN-chan reasoning - complex reasoning tasks
    /// </summary>
    SnChanReasoning = 11,

    /// <summary>
    /// SN-chan vision - image analysis for users
    /// </summary>
    SnChanVision = 12,

    /// <summary>
    /// System task - internal system operations
    /// </summary>
    SystemTask = 20,

    /// <summary>
    /// Embedding generation - vector embedding creation
    /// </summary>
    Embedding = 30
}

/// <summary>
/// Extension methods for ModelUseCase
/// </summary>
public static class ModelUseCaseExtensions
{
    /// <summary>
    /// Gets a display name for the use case
    /// </summary>
    public static string GetDisplayName(this ModelUseCase useCase) => useCase switch
    {
        ModelUseCase.MiChanChat => "MiChan Chat",
        ModelUseCase.MiChanAutonomous => "MiChan Autonomous",
        ModelUseCase.MiChanVision => "MiChan Vision",
        ModelUseCase.MiChanScheduledTask => "MiChan Scheduled Task",
        ModelUseCase.MiChanCompaction => "MiChan Compaction",
        ModelUseCase.MiChanTopicGeneration => "MiChan Topic Generation",
        ModelUseCase.SnChanChat => "SN-chan Chat",
        ModelUseCase.SnChanReasoning => "SN-chan Reasoning",
        ModelUseCase.SnChanVision => "SN-chan Vision",
        ModelUseCase.SystemTask => "System Task",
        ModelUseCase.Embedding => "Embedding",
        _ => "Default"
    };

    /// <summary>
    /// Checks if this is a MiChan-related use case
    /// </summary>
    public static bool IsMiChanUseCase(this ModelUseCase useCase) =>
        useCase is >= ModelUseCase.MiChanChat and <= ModelUseCase.MiChanTopicGeneration;

    /// <summary>
    /// Checks if this is an SN-chan-related use case
    /// </summary>
    public static bool IsSnChanUseCase(this ModelUseCase useCase) =>
        useCase is >= ModelUseCase.SnChanChat and <= ModelUseCase.SnChanVision;
}
