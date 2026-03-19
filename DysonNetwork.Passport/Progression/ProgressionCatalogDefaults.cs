using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Progression;

public static class ProgressionCatalogDefaults
{
    public static readonly ProgressionSeedSettings Settings = new()
    {
        SourcePointCurrency = WalletCurrency.SourcePoint,
        CompletionPacketType = WebSocketPacketType.ProgressionCompleted,
        DefaultTimeZone = "UTC"
    };

    public static readonly List<AchievementSeedDefinition> Achievements =
    [
        new()
        {
            Identifier = "first-post",
            Title = "First Light",
            Summary = "Publish your first post.",
            Icon = "ink",
            SortOrder = 10,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.first-post",
                    Label = "First Light",
                    Caption = "Published a first post."
                }
            }
        },
        new()
        {
            Identifier = "first-reaction",
            Title = "Spark",
            Summary = "React to a post for the first time.",
            Icon = "spark",
            SortOrder = 20,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostReact] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 30,
                SourcePoints = 3,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.first-reaction",
                    Label = "Spark",
                    Caption = "Shared a first reaction."
                }
            }
        },
        new()
        {
            Identifier = "social-butterfly",
            Title = "Social Butterfly",
            Summary = "Join three realms.",
            Icon = "globe",
            SortOrder = 30,
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.RealmJoin] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 120,
                SourcePoints = 12,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.social-butterfly",
                    Label = "Social Butterfly",
                    Caption = "Joined three realms."
                }
            }
        },
        new()
        {
            Identifier = "publisher-founder",
            Title = "Founder",
            Summary = "Create a publisher.",
            Icon = "flag",
            SortOrder = 40,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PublisherCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 150,
                SourcePoints = 15,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.publisher-founder",
                    Label = "Founder",
                    Caption = "Created a publisher."
                }
            }
        },
        new()
        {
            Identifier = "realm-citizen",
            Title = "Realm Citizen",
            Summary = "Create your first realm.",
            Icon = "castle",
            SortOrder = 50,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.RealmCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 150,
                SourcePoints = 15,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.realm-citizen",
                    Label = "Realm Citizen",
                    Caption = "Created a realm."
                }
            }
        },
        new()
        {
            Identifier = "chat-regular",
            Title = "Chat Regular",
            Summary = "Use chat on five separate cooldown ticks.",
            Icon = "message-circle",
            SortOrder = 60,
            TargetCount = 5,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 80,
                SourcePoints = 8,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.chat-regular",
                    Label = "Chat Regular",
                    Caption = "Stayed active in chat."
                }
            }
        },
        new()
        {
            Identifier = "chatterbox",
            Title = "Chatterbox",
            Summary = "Use chat on twenty separate cooldown ticks.",
            Icon = "messages-square",
            SortOrder = 70,
            TargetCount = 20,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 180,
                SourcePoints = 18,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.chatterbox",
                    Label = "Chatterbox",
                    Caption = "Kept conversations moving."
                }
            }
        },
        new()
        {
            Identifier = "community-joiner",
            Title = "Community Joiner",
            Summary = "Join your first publisher.",
            Icon = "users",
            SortOrder = 80,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PublisherMemberJoin] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 90,
                SourcePoints = 9,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.community-joiner",
                    Label = "Community Joiner",
                    Caption = "Joined a publisher."
                }
            }
        },
        new()
        {
            Identifier = "spring-rally-2026",
            Title = "Spring Rally 2026",
            Summary = "Join the 2026 spring event by triggering chat activity three times between March 20, 2026 and April 20, 2026.",
            Icon = "flowers",
            SortOrder = 90,
            AvailableFrom = Instant.FromUtc(2026, 3, 20, 0, 0),
            AvailableUntil = Instant.FromUtc(2026, 4, 20, 23, 59),
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 120,
                SourcePoints = 12,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.spring-rally-2026",
                    Label = "Spring Rally 2026",
                    Caption = "Joined the 2026 spring rally."
                }
            }
        },
        new()
        {
            Identifier = "new-year-flashback-2026",
            Title = "New Year Flashback 2026",
            Summary = "A retired 2026 new year event achievement kept for historical rewards.",
            Icon = "party-popper",
            SortOrder = 100,
            IsProgressEnabled = false,
            AvailableFrom = Instant.FromUtc(2026, 1, 1, 0, 0),
            AvailableUntil = Instant.FromUtc(2026, 1, 10, 23, 59),
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 75,
                SourcePoints = 6,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.new-year-flashback-2026",
                    Label = "New Year Flashback 2026",
                    Caption = "Completed the 2026 new year event."
                }
            }
        }
    ];

    public static readonly List<QuestSeedDefinition> Quests =
    [
        new()
        {
            Identifier = "daily-post",
            Title = "Daily Dispatch",
            Summary = "Publish one post today.",
            Icon = "calendar",
            SortOrder = 10,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Daily },
            Reward = new SnProgressRewardDefinition { Experience = 25, SourcePoints = 2, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "daily-react-3",
            Title = "Warm Welcome",
            Summary = "React to three posts today.",
            Icon = "heart",
            SortOrder = 20,
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostReact] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Daily },
            Reward = new SnProgressRewardDefinition { Experience = 40, SourcePoints = 4, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "weekly-discussion",
            Title = "Weekly Threadstarter",
            Summary = "Create two posts this week.",
            Icon = "discussion",
            SortOrder = 30,
            TargetCount = 2,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Weekly },
            Reward = new SnProgressRewardDefinition { Experience = 80, SourcePoints = 8, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "weekly-publisher-participation",
            Title = "Publisher Ally",
            Summary = "Join a publisher this week.",
            Icon = "badge",
            SortOrder = 40,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PublisherMemberJoin] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Weekly },
            Reward = new SnProgressRewardDefinition { Experience = 70, SourcePoints = 7, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "monthly-realm-engagement",
            Title = "Realm Regular",
            Summary = "Join three realms this month.",
            Icon = "orbit",
            SortOrder = 50,
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.RealmJoin] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Monthly },
            Reward = new SnProgressRewardDefinition { Experience = 150, SourcePoints = 15, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "daily-chat-burst",
            Title = "Daily Chat Burst",
            Summary = "Trigger chat activity twice today.",
            Icon = "message-square",
            SortOrder = 60,
            TargetCount = 2,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Daily },
            Reward = new SnProgressRewardDefinition { Experience = 30, SourcePoints = 3, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "spring-rally-daily-2026",
            Title = "Spring Rally Daily 2026",
            Summary = "Trigger chat activity twice per day during the spring event window.",
            Icon = "confetti",
            SortOrder = 70,
            AvailableFrom = Instant.FromUtc(2026, 3, 20, 0, 0),
            AvailableUntil = Instant.FromUtc(2026, 4, 20, 23, 59),
            TargetCount = 2,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Daily },
            Reward = new SnProgressRewardDefinition { Experience = 45, SourcePoints = 5, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "anniversary-archive-2026",
            Title = "Anniversary Archive 2026",
            Summary = "A retired anniversary quest kept visible for historical reward records.",
            Icon = "calendar-heart",
            SortOrder = 80,
            IsProgressEnabled = false,
            AvailableFrom = Instant.FromUtc(2026, 2, 1, 0, 0),
            AvailableUntil = Instant.FromUtc(2026, 2, 7, 23, 59),
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Daily },
            Reward = new SnProgressRewardDefinition { Experience = 35, SourcePoints = 4, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "weekly-chat-regular",
            Title = "Weekly Chat Regular",
            Summary = "Trigger chat activity ten times this week.",
            Icon = "messages-square",
            SortOrder = 70,
            TargetCount = 10,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Weekly },
            Reward = new SnProgressRewardDefinition { Experience = 90, SourcePoints = 9, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
        new()
        {
            Identifier = "monthly-community-tour",
            Title = "Community Tour",
            Summary = "Join two publishers this month.",
            Icon = "map",
            SortOrder = 80,
            TargetCount = 2,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PublisherMemberJoin] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Monthly },
            Reward = new SnProgressRewardDefinition { Experience = 120, SourcePoints = 12, SourcePointsCurrency = WalletCurrency.SourcePoint }
        }
    ];
}
