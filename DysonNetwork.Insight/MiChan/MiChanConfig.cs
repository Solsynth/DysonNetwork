using System.ComponentModel.DataAnnotations;
using DysonNetwork.Insight.Agent.Models;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Configuration for MiChan AI agent
/// </summary>
public class MiChanConfig : IValidatableObject
{
    public bool Enabled { get; set; } = false;
    public string GatewayUrl { get; set; } = "http://localhost:5070";
    public string WebSocketUrl { get; set; } = "ws://localhost:5070/ws";
    public string AccessToken { get; set; } = "";
    public string BotAccountId { get; set; } = "";
    public string BotPublisherId { get; set; } = ""; // Publisher ID for posting (different from AccountId)

    /// <summary>
    /// Primary model for chat conversations. Defaults to deepseek-chat.
    /// </summary>
    public ModelConfiguration ThinkingModel { get; set; } = ModelRegistry.DeepSeekChat;

    /// <summary>
    /// Model for autonomous behavior. Falls back to ThinkingModel if not set.
    /// </summary>
    public ModelConfiguration? AutonomousModel { get; set; }

    /// <summary>
    /// Model for scheduled tasks. Falls back to ThinkingModel if not set.
    /// </summary>
    public ModelConfiguration? ScheduledTaskModel { get; set; }

    /// <summary>
    /// Model for conversation compaction/summarization. Falls back to ThinkingModel if not set.
    /// </summary>
    public ModelConfiguration? CompactionModel { get; set; }

    /// <summary>
    /// Model for topic generation. Falls back to ThinkingModel if not set.
    /// </summary>
    public ModelConfiguration? TopicGenerationModel { get; set; }

    public string Personality { get; set; } = "";
    public string? PersonalityFile { get; set; }
    public MiChanAutoRespondConfig AutoRespond { get; set; } = new();
    public MiChanAutonomousBehaviorConfig AutonomousBehavior { get; set; } = new();
    public MiChanPostMonitoringConfig PostMonitoring { get; set; } = new();
    public MiChanMemoryConfig Memory { get; set; } = new();
    public MiChanVisionConfig Vision { get; set; } = new();

    /// <summary>
    /// Gets the effective autonomous model (falls back to ThinkingModel)
    /// </summary>
    public ModelConfiguration GetAutonomousModel() =>
        AutonomousModel ?? ThinkingModel;

    /// <summary>
    /// Gets the effective scheduled task model (falls back to ThinkingModel)
    /// </summary>
    public ModelConfiguration GetScheduledTaskModel() =>
        ScheduledTaskModel ?? ThinkingModel;

    /// <summary>
    /// Gets the effective compaction model (falls back to ThinkingModel)
    /// </summary>
    public ModelConfiguration GetCompactionModel() =>
        CompactionModel ?? ThinkingModel;

    /// <summary>
    /// Gets the effective topic generation model (falls back to ThinkingModel)
    /// </summary>
    public ModelConfiguration GetTopicGenerationModel() =>
        TopicGenerationModel ?? ThinkingModel;

    /// <summary>
    /// Gets the vision model configuration
    /// </summary>
    public ModelConfiguration GetVisionModel() =>
        new ModelConfiguration
        {
            ModelId = Vision.VisionThinkingService,
            Temperature = 0.7,
            EnableFunctions = false
        };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();

        if (Enabled)
        {
            // Validate ThinkingModel
            var thinkingResults = ThinkingModel.Validate(validationContext).ToList();
            results.AddRange(thinkingResults.Select(r =>
                new ValidationResult($"[ThinkingModel] {r.ErrorMessage}", r.MemberNames)));

            // Validate optional models if set
            if (AutonomousModel != null)
            {
                var autonomousResults = AutonomousModel.Validate(validationContext).ToList();
                results.AddRange(autonomousResults.Select(r =>
                    new ValidationResult($"[AutonomousModel] {r.ErrorMessage}", r.MemberNames)));
            }

            // Validate required credentials
            if (string.IsNullOrEmpty(AccessToken))
            {
                results.Add(new ValidationResult(
                    "AccessToken is required when MiChan is enabled",
                    new[] { nameof(AccessToken) }));
            }

            if (string.IsNullOrEmpty(BotAccountId))
            {
                results.Add(new ValidationResult(
                    "BotAccountId is required when MiChan is enabled",
                    new[] { nameof(BotAccountId) }));
            }

            // Validate vision model
            if (Vision.EnableVisionAnalysis && !ModelRegistry.IsValid(Vision.VisionThinkingService))
            {
                results.Add(new ValidationResult(
                    $"Vision model '{Vision.VisionThinkingService}' is not registered",
                    new[] { nameof(Vision) + "." + nameof(Vision.VisionThinkingService) }));
            }
        }

        return results;
    }
}

public class MiChanAutoRespondConfig
{
    public bool ToChatMessages { get; set; } = true;
    public bool ToMentions { get; set; } = true;
    public bool ToDirectMessages { get; set; } = true;
}

public class MiChanAutonomousBehaviorConfig
{
    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; } = false;
    public int FixedIntervalMinutes { get; set; } = 10; // Fixed interval (0 = use random interval)
    public int MinIntervalMinutes { get; set; } = 10;
    public int MaxIntervalMinutes { get; set; } = 60;
    public List<string> Actions { get; set; } = ["browse", "react", "create_post", "pin", "repost", "start_conversation"];
    public string PersonalityMood { get; set; } = "curious, friendly, occasionally philosophical";
    public int MinRepostAgeDays { get; set; } = 3; // Minimum age of post before reposting (days)
    public MiChanDynamicMoodConfig DynamicMood { get; set; } = new();

    // Settings for proactive conversation behavior
    public int MaxConversationsPerDay { get; set; } = 3; // Maximum conversations MiChan can initiate per day
    public int MinHoursSinceLastContact { get; set; } = 24; // Minimum hours between contacting the same user
    public int ConversationProbability { get; set; } = 10; // % chance to attempt starting a conversation per cycle

    // Action probabilities (% chance when action is selected)
    public int ReplyProbability { get; set; } = 30; // % chance to reply to a post (when not mentioned)
    public int RepostProbability { get; set; } = 15; // % chance to check for and repost interesting content
    public int CreatePostProbability { get; set; } = 20; // % chance to create an autonomous post
}

public class MiChanDynamicMoodConfig
{
    public bool Enabled { get; set; } = true;
    public int UpdateIntervalMinutes { get; set; } = 30; // How often mood can update (time-based)
    public int MinUpdateIntervalMinutes { get; set; } = 15; // Minimum time between mood updates
    public int MinInteractionsForUpdate { get; set; } = 5; // Minimum interactions before mood update
    public string BasePersonality { get; set; } = "curious, friendly, occasionally philosophical";
}

public class MiChanPostMonitoringConfig
{
    public bool Enabled { get; set; } = true;
    public int MentionResponseTimeoutSeconds { get; set; } = 30;
}

public class MiChanMemoryConfig
{
    public int MaxContextLength { get; set; } = 100;
    public bool PersistToDatabase { get; set; } = true;
    public bool EnableSemanticSearch { get; set; } = true;
    public double MinSimilarityThreshold { get; set; } = 0.7;
    public int SemanticSearchLimit { get; set; } = 5;
    public TimeSpan? MaxMemoryAge { get; set; }
}

public class MiChanVisionConfig
{
    /// <summary>
    /// Vision model ID from ModelRegistry (e.g., "vision-openrouter", "vision-aliyun")
    /// </summary>
    public string VisionThinkingService { get; set; } = ModelRegistry.ClaudeOpus.Id;

    /// <summary>
    /// Whether to enable image analysis capabilities
    /// </summary>
    public bool EnableVisionAnalysis { get; set; } = true;

    /// <summary>
    /// Maximum number of images to process in a single request
    /// </summary>
    public int MaxImagesPerRequest { get; set; } = 10;

    /// <summary>
    /// Whether to fallback to text-only model if vision model is unavailable
    /// </summary>
    public bool FallbackToTextModel { get; set; } = true;
}
