using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public abstract class ProgressionDefinitionType
{
    public const string Achievement = "achievement";
    public const string Quest = "quest";
}

public abstract class QuestRepeatability
{
    public const string None = "none";
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
}

public class SnProgressTriggerDefinition
{
    public List<string> Actions { get; set; } = [];
    public Dictionary<string, string> MetaEquals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class SnQuestScheduleConfig
{
    [MaxLength(32)] public string Repeatability { get; set; } = QuestRepeatability.None;
    public List<int> ActiveDaysOfWeek { get; set; } = [];
}

public class SnProgressBadgeRewardDefinition
{
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(1024)] public string? Label { get; set; }
    [MaxLength(4096)] public string? Caption { get; set; }
    public Dictionary<string, object?> Meta { get; set; } = new();
}

public class SnProgressRewardDefinition
{
    public long Experience { get; set; }
    public decimal SourcePoints { get; set; }
    [MaxLength(128)] public string SourcePointsCurrency { get; set; } = WalletCurrency.SourcePoint;
    public SnProgressBadgeRewardDefinition? Badge { get; set; }
}

[Index(nameof(Identifier), IsUnique = true)]
public class SnAchievementDefinition : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string Identifier { get; set; } = null!;
    [MaxLength(256)] public string Title { get; set; } = null!;
    [MaxLength(4096)] public string Summary { get; set; } = null!;
    [MaxLength(256)] public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSeedManaged { get; set; } = true;
    public int TargetCount { get; set; } = 1;
    [Column(TypeName = "jsonb")] public SnProgressTriggerDefinition Trigger { get; set; } = new();
    [Column(TypeName = "jsonb")] public SnProgressRewardDefinition Reward { get; set; } = new();
}

[Index(nameof(Identifier), IsUnique = true)]
public class SnQuestDefinition : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(256)] public string Identifier { get; set; } = null!;
    [MaxLength(256)] public string Title { get; set; } = null!;
    [MaxLength(4096)] public string Summary { get; set; } = null!;
    [MaxLength(256)] public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSeedManaged { get; set; } = true;
    public int TargetCount { get; set; } = 1;
    [Column(TypeName = "jsonb")] public SnProgressTriggerDefinition Trigger { get; set; } = new();
    [Column(TypeName = "jsonb")] public SnQuestScheduleConfig Schedule { get; set; } = new();
    [Column(TypeName = "jsonb")] public SnProgressRewardDefinition Reward { get; set; } = new();
}

[Index(nameof(AccountId), nameof(AchievementDefinitionId), IsUnique = true)]
public class SnAccountAchievement : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid AchievementDefinitionId { get; set; }
    public int ProgressCount { get; set; }
    public Instant? CompletedAt { get; set; }
    public Instant? ClaimedAt { get; set; }
    [MaxLength(256)] public string? LastRewardToken { get; set; }
}

[Index(nameof(AccountId), nameof(QuestDefinitionId), nameof(PeriodKey), IsUnique = true)]
public class SnAccountQuestProgress : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid QuestDefinitionId { get; set; }
    [MaxLength(128)] public string PeriodKey { get; set; } = null!;
    public int ProgressCount { get; set; }
    public int RepeatIterationCount { get; set; }
    public Instant? CompletedAt { get; set; }
    public Instant? ClaimedAt { get; set; }
    [MaxLength(256)] public string? LastRewardToken { get; set; }
}

[Index(nameof(RewardToken), IsUnique = true)]
public class SnProgressRewardGrant : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [MaxLength(32)] public string DefinitionType { get; set; } = ProgressionDefinitionType.Achievement;
    [MaxLength(256)] public string DefinitionIdentifier { get; set; } = null!;
    [MaxLength(256)] public string DefinitionTitle { get; set; } = null!;
    [MaxLength(256)] public string RewardToken { get; set; } = null!;
    public Guid SourceEventId { get; set; }
    [Column(TypeName = "jsonb")] public SnProgressRewardDefinition Reward { get; set; } = new();
    [MaxLength(64)] public string? PeriodKey { get; set; }
    public Instant? BadgeGrantedAt { get; set; }
    public Instant? ExperienceGrantedAt { get; set; }
    public Instant? SourcePointsGrantedAt { get; set; }
    public Instant? NotificationSentAt { get; set; }
}

[Index(nameof(EventId), nameof(DefinitionType), nameof(DefinitionIdentifier), nameof(PeriodKey), IsUnique = true)]
public class SnProgressEventReceipt : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid AccountId { get; set; }
    [MaxLength(32)] public string DefinitionType { get; set; } = ProgressionDefinitionType.Achievement;
    [MaxLength(256)] public string DefinitionIdentifier { get; set; } = null!;
    [MaxLength(64)] public string PeriodKey { get; set; } = string.Empty;
}

public class ProgressionSeedOptions
{
    public ProgressionSeedSettings Settings { get; set; } = new();
    public List<AchievementSeedDefinition> Achievements { get; set; } = [];
    public List<QuestSeedDefinition> Quests { get; set; } = [];
}

public class ProgressionSeedSettings
{
    [MaxLength(128)] public string SourcePointCurrency { get; set; } = WalletCurrency.SourcePoint;
    [MaxLength(64)] public string CompletionPacketType { get; set; } = WebSocketPacketType.ProgressionCompleted;
    [MaxLength(128)] public string DefaultTimeZone { get; set; } = "UTC";
}

public class AchievementSeedDefinition
{
    public string Identifier { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSeedManaged { get; set; } = true;
    public int TargetCount { get; set; } = 1;
    public SnProgressTriggerDefinition Trigger { get; set; } = new();
    public SnProgressRewardDefinition Reward { get; set; } = new();
}

public class QuestSeedDefinition
{
    public string Identifier { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Summary { get; set; } = null!;
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool Hidden { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSeedManaged { get; set; } = true;
    public int TargetCount { get; set; } = 1;
    public SnProgressTriggerDefinition Trigger { get; set; } = new();
    public SnQuestScheduleConfig Schedule { get; set; } = new();
    public SnProgressRewardDefinition Reward { get; set; } = new();
}
