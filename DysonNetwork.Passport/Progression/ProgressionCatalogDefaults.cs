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
            Identifier = "initial-post",
            Title = "Initial Commit",
            Summary = "Say hello to the Solar Network world!",
            Icon = "ink",
            SortOrder = 100,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 10,
                SourcePointsCurrency = WalletCurrency.SourcePoint
            }
        },
        new()
        {
            Identifier = "expert-post",
            Title = "Better than 陆游 (Luyou)",
            Summary = "Wrote posts more than 陆游.\n*Luyou* wrote over 9300+ poems in his life.",
            Icon = "ink",
            SortOrder = 200,
            TargetCount = 9362,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 9362,
                SourcePoints = 100,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.post.expert",
                    Label = "Better than 陆游",
                    Caption = "Created over 9362 posts",
                }
            }
        },
        new()
        {
            Identifier = "initial-reaction",
            Title = "People have emotion",
            Summary = "React to a post for the first time.",
            Icon = "spark",
            SortOrder = 101,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostReact] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint
            }
        },
        new()
        {
            Identifier = "initial-realm-join",
            Title = "Horizons, broadened.",
            Summary = "Join a realm.",
            Icon = "globe",
            SortOrder = 103,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.RealmJoin] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 10,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            }
        },
        new()
        {
            Identifier = "initial-publisher",
            Title = "Startup Company",
            Summary = "Not backed by y combinator.\nCreate a publisher.",
            Icon = "flag",
            SortOrder = 102,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PublisherCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 10,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            }
        },
        new()
        {
            Identifier = "initial-chat",
            Title = "Gugugaga",
            Summary = "Use chat for the first time.",
            Icon = "message-circle",
            SortOrder = 104,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            }
        },
        new()
        {
            Identifier = "expert-chat",
            Title = "Never touch grass",
            Summary = "Use chat over 10k times.",
            Icon = "messages-square",
            SortOrder = 201,
            TargetCount = 10000,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 180,
                SourcePoints = 18,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.chat.expert",
                    Label = "No-life's Otaku",
                    Caption = "I can't believe some one spend for 10k minutes chatting on the Solar Network."
                }
            }
        },
        // new()
        // {
        //     Identifier = "spring-rally-2026",
        //     Title = "Spring Rally 2026",
        //     Summary =
        //         "Join the 2026 spring event by triggering chat activity three times between March 20, 2026 and April 20, 2026.",
        //     Icon = "flowers",
        //     SortOrder = 90,
        //     AvailableFrom = Instant.FromUtc(2026, 3, 20, 0, 0),
        //     AvailableUntil = Instant.FromUtc(2026, 4, 20, 23, 59),
        //     TargetCount = 3,
        //     Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
        //     Reward = new SnProgressRewardDefinition
        //     {
        //         Experience = 120,
        //         SourcePoints = 12,
        //         SourcePointsCurrency = WalletCurrency.SourcePoint,
        //         Badge = new SnProgressBadgeRewardDefinition
        //         {
        //             Type = "progression.spring-rally-2026",
        //             Label = "Spring Rally 2026",
        //             Caption = "Joined the 2026 spring rally."
        //         }
        //     }
        // },
        // new()
        // {
        //     Identifier = "new-year-flashback-2026",
        //     Title = "New Year Flashback 2026",
        //     Summary = "A retired 2026 new year event achievement kept for historical rewards.",
        //     Icon = "party-popper",
        //     SortOrder = 100,
        //     IsProgressEnabled = false,
        //     AvailableFrom = Instant.FromUtc(2026, 1, 1, 0, 0),
        //     AvailableUntil = Instant.FromUtc(2026, 1, 10, 23, 59),
        //     Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
        //     Reward = new SnProgressRewardDefinition
        //     {
        //         Experience = 75,
        //         SourcePoints = 6,
        //         SourcePointsCurrency = WalletCurrency.SourcePoint,
        //         Badge = new SnProgressBadgeRewardDefinition
        //         {
        //             Type = "progression.new-year-flashback-2026",
        //             Label = "New Year Flashback 2026",
        //             Caption = "Completed the 2026 new year event."
        //         }
        //     }
        // }
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
            Reward = new SnProgressRewardDefinition
                { Experience = 25, SourcePoints = 2, SourcePointsCurrency = WalletCurrency.SourcePoint }
        },
    ];
}