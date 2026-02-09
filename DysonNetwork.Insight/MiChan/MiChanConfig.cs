namespace DysonNetwork.Insight.MiChan;

public class MiChanConfig
{
    public bool Enabled { get; set; } = false;
    public string GatewayUrl { get; set; } = "http://localhost:5070";
    public string WebSocketUrl { get; set; } = "ws://localhost:5070/ws";
    public string AccessToken { get; set; } = "";
    public string BotAccountId { get; set; } = "";
    public string BotPublisherId { get; set; } = ""; // Publisher ID for posting (different from AccountId)
    public string ThinkingService { get; set; } = "deepseek-chat";
    public string Personality { get; set; } = "";
    public string? PersonalityFile { get; set; }
    public MiChanAutoRespondConfig AutoRespond { get; set; } = new();
    public MiChanAutonomousBehaviorConfig AutonomousBehavior { get; set; } = new();
    public MiChanPostMonitoringConfig PostMonitoring { get; set; } = new();
    public MiChanMemoryConfig Memory { get; set; } = new();
    public MiChanVisionConfig Vision { get; set; } = new();
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
    public bool DryRun { get; set; } = false; // If true, no real posts/actions will be created
    public int MinIntervalMinutes { get; set; } = 10;
    public int MaxIntervalMinutes { get; set; } = 60;
    public List<string> Actions { get; set; } = ["browse", "react", "create_post", "pin", "repost"];
    public string PersonalityMood { get; set; } = "curious, friendly, occasionally philosophical";
    public int MinRepostAgeDays { get; set; } = 3; // Minimum age of post before reposting (days)
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
    public string VisionThinkingService { get; set; } = "vision-openrouter";
    public bool EnableVisionAnalysis { get; set; } = true;
}
