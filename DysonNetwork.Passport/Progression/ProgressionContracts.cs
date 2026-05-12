using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Progression;

public class ProgressionDefinitionUpsertRequest
{
    public string Identifier { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string? Icon { get; set; }
    public string? SeriesIdentifier { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeriesOrder { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSeedManaged { get; set; }
    public bool IsProgressEnabled { get; set; } = true;
    public Instant? AvailableFrom { get; set; }
    public Instant? AvailableUntil { get; set; }
    public int TargetCount { get; set; } = 1;
    public SnProgressTriggerDefinition Trigger { get; set; } = new();
    public SnProgressRewardDefinition Reward { get; set; } = new();
}

public class QuestDefinitionUpsertRequest : ProgressionDefinitionUpsertRequest
{
    public SnQuestScheduleConfig Schedule { get; set; } = new();
}

public class ProgressionAchievementState
{
    public string Identifier { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string? Icon { get; set; }
    public string? SeriesIdentifier { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeriesOrder { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsProgressEnabled { get; set; }
    public bool IsCurrentlyAvailable { get; set; }
    public Instant? AvailableFrom { get; set; }
    public Instant? AvailableUntil { get; set; }
    public int TargetCount { get; set; }
    public int ProgressCount { get; set; }
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public int SeriesTotalSteps { get; set; } = 1;
    public int SeriesCompletedSteps { get; set; }
    public List<ProgressionSeriesStage> SeriesStages { get; set; } = [];
    public bool IsCompleted { get; set; }
    public Instant? CompletedAt { get; set; }
    public SnProgressRewardDefinition Reward { get; set; } = new();
}

public class ProgressionQuestState
{
    public string Identifier { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string? Icon { get; set; }
    public string? SeriesIdentifier { get; set; }
    public string? SeriesTitle { get; set; }
    public int? SeriesOrder { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsProgressEnabled { get; set; }
    public bool IsCurrentlyAvailable { get; set; }
    public Instant? AvailableFrom { get; set; }
    public Instant? AvailableUntil { get; set; }
    public int TargetCount { get; set; }
    public int ProgressCount { get; set; }
    public int SeriesTotalSteps { get; set; } = 1;
    public int SeriesCompletedSteps { get; set; }
    public List<ProgressionSeriesStage> SeriesStages { get; set; } = [];
    public bool IsCompleted { get; set; }
    public Instant? CompletedAt { get; set; }
    public string PeriodKey { get; set; } = string.Empty;
    public Instant NextResetAt { get; set; }
    public SnQuestScheduleConfig Schedule { get; set; } = new();
    public SnProgressRewardDefinition Reward { get; set; } = new();
}

public class ProgressionSeriesStage
{
    public string Identifier { get; set; } = null!;
    public string Title { get; set; } = null!;
    public int? SeriesOrder { get; set; }
    public int TargetCount { get; set; }
    public bool IsCompleted { get; set; }
    public Instant? CompletedAt { get; set; }
}

public class ProgressionCompletionPacket
{
    public string Kind { get; set; } = null!;
    public string Identifier { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? PeriodKey { get; set; }
    public SnProgressRewardDefinition Reward { get; set; } = new();
}
