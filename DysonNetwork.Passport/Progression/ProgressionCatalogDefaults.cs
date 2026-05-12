using DysonNetwork.Shared.Models;

namespace DysonNetwork.Passport.Progression;

public static class ProgressionCatalogDefaults
{
    public static readonly ProgressionSeedSettings Settings = new()
    {
        SourcePointCurrency = WalletCurrency.SourcePoint,
        CompletionPacketType = WebSocketPacketType.ProgressionCompleted,
        DefaultTimeZone = "UTC",
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
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
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
                },
            },
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
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
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
            },
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
            },
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
            },
        },
        new()
        {
            Identifier = "initial-featured-post",
            Title = "Editor's Pick",
            Summary = "Get one of your posts featured.",
            Icon = "sparkles",
            SeriesIdentifier = "featured-post",
            SeriesTitle = "Featured Posts",
            SeriesOrder = 1,
            SortOrder = 105,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostFeatured] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 150,
                SourcePoints = 15,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.post.featured",
                    Label = "Editor's Pick",
                    Caption = "Created a post that got featured.",
                },
            },
        },
        new()
        {
            Identifier = "streak-post-3",
            Title = "Three-day Build",
            Summary = "Create posts for 3 days in a row.",
            Icon = "calendar-heart",
            SeriesIdentifier = "post-streak",
            SeriesTitle = "Posting Streak",
            SeriesOrder = 1,
            SortOrder = 106,
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.PostCreate],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 200,
                SourcePoints = 20,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "streak-post-7",
            Title = "One-week Columnist",
            Summary = "Create posts for 7 days in a row.",
            Icon = "calendar-range",
            SeriesIdentifier = "post-streak",
            SeriesTitle = "Posting Streak",
            SeriesOrder = 2,
            SortOrder = 107,
            TargetCount = 7,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.PostCreate],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 500,
                SourcePoints = 35,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "streak-post-30",
            Title = "Serial Publisher",
            Summary = "Create posts for 30 days in a row.",
            Icon = "calendar-fold",
            SeriesIdentifier = "post-streak",
            SeriesTitle = "Posting Streak",
            SeriesOrder = 3,
            SortOrder = 108,
            TargetCount = 30,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.PostCreate],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 3000,
                SourcePoints = 90,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.post.streak.30",
                    Label = "Serial Publisher",
                    Caption = "Created posts for 30 days in a row.",
                },
            },
        },
        new()
        {
            Identifier = "streak-login-7",
            Title = "Daily Standup",
            Summary = "Stay active for 7 days in a row.",
            Icon = "sunrise",
            SeriesIdentifier = "activity-streak",
            SeriesTitle = "Activity Streak",
            SeriesOrder = 1,
            SortOrder = 109,
            TargetCount = 7,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.AccountActive],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 250,
                SourcePoints = 20,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "streak-login-30",
            Title = "Always Online",
            Summary = "Stay active for 30 days in a row.",
            Icon = "sun-medium",
            SeriesIdentifier = "activity-streak",
            SeriesTitle = "Activity Streak",
            SeriesOrder = 2,
            SortOrder = 110,
            TargetCount = 30,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.AccountActive],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 1200,
                SourcePoints = 60,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "streak-login-365",
            Title = "Still Here",
            Summary = "Stay active for 365 days in a row.",
            Icon = "sun",
            SeriesIdentifier = "activity-streak",
            SeriesTitle = "Activity Streak",
            SeriesOrder = 3,
            SortOrder = 210,
            TargetCount = 365,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.AccountActive],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 12000,
                SourcePoints = 365,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.login.streak.365",
                    Label = "Still Here",
                    Caption = "Logged in for 365 days in a row.",
                },
            },
        },
        new()
        {
            Identifier = "expert-featured-post",
            Title = "Hall of Fame",
            Summary = "Get 100 of your posts featured.",
            Icon = "sparkles",
            SeriesIdentifier = "featured-post",
            SeriesTitle = "Featured Posts",
            SeriesOrder = 2,
            SortOrder = 205,
            TargetCount = 100,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostFeatured] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 5000,
                SourcePoints = 100,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.post.featured.expert",
                    Label = "Hall of Fame",
                    Caption = "Created 100 featured posts.",
                },
            },
        },
        new()
        {
            Identifier = "stellar-supporter-1",
            Title = "Supporter",
            Summary = "Purchase one eligible month of the Stellar Program.",
            Icon = "badge-cent",
            SeriesIdentifier = "stellar-supporter",
            SeriesTitle = "Stellar Supporter",
            SeriesOrder = 1,
            SortOrder = 120,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.StellarSupportMonth],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 120,
                SourcePoints = 12,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "stellar-supporter-3",
            Title = "Backer",
            Summary = "Purchase 3 eligible months of the Stellar Program.",
            Icon = "gem",
            SeriesIdentifier = "stellar-supporter",
            SeriesTitle = "Stellar Supporter",
            SeriesOrder = 2,
            SortOrder = 121,
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.StellarSupportMonth],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 360,
                SourcePoints = 24,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "stellar-supporter-6",
            Title = "Patron",
            Summary = "Purchase 6 eligible months of the Stellar Program.",
            Icon = "medal",
            SeriesIdentifier = "stellar-supporter",
            SeriesTitle = "Stellar Supporter",
            SeriesOrder = 3,
            SortOrder = 122,
            TargetCount = 6,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.StellarSupportMonth],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 900,
                SourcePoints = 48,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "stellar-supporter-9",
            Title = "Celestial Patron",
            Summary = "Purchase 9 eligible months of the Stellar Program.",
            Icon = "orbit",
            SeriesIdentifier = "stellar-supporter",
            SeriesTitle = "Stellar Supporter",
            SeriesOrder = 4,
            SortOrder = 123,
            TargetCount = 9,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.StellarSupportMonth],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 1500,
                SourcePoints = 72,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "stellar-supporter-12",
            Title = "Mega Supporter",
            Summary = "Purchase 12 eligible months of the Stellar Program.",
            Icon = "crown",
            SeriesIdentifier = "stellar-supporter",
            SeriesTitle = "Stellar Supporter",
            SeriesOrder = 5,
            SortOrder = 124,
            TargetCount = 12,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.StellarSupportMonth],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 2400,
                SourcePoints = 120,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.stellar.supporter.12",
                    Label = "Mega Supporter",
                    Caption = "Purchased 12 eligible months of the Stellar Program.",
                },
            },
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
                    Caption =
                        "I can't believe some one spend for 10k minutes chatting on the Solar Network.",
                },
            },
        },
        new()
        {
            Identifier = "first-avatar",
            Title = "Picture Perfect",
            Summary = "Set your profile picture for the first time.",
            Icon = "image",
            SortOrder = 110,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.AccountAvatar] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.account.avatar",
                    Label = "Picture Perfect",
                    Caption = "Set a profile picture for the first time.",
                },
            },
        },
        new()
        {
            Identifier = "first-connection",
            Title = "Getting Connected",
            Summary = "Link an external account for the first time.",
            Icon = "link",
            SortOrder = 111,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.AccountConnectionLink] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "first-push",
            Title = "Always Informed",
            Summary = "Enable push notifications for the first time.",
            Icon = "bell",
            SortOrder = 112,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.AccountPushEnable] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 30,
                SourcePoints = 3,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "friends-1",
            Title = "It Takes Two",
            Summary = "Make your first friend.",
            Icon = "user-plus",
            SeriesIdentifier = "friend-count",
            SeriesTitle = "Friend Count",
            SeriesOrder = 1,
            SortOrder = 130,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.RelationshipFriendEstablished],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "friends-5",
            Title = "Social Butterfly",
            Summary = "Make 5 friends.",
            Icon = "users",
            SeriesIdentifier = "friend-count",
            SeriesTitle = "Friend Count",
            SeriesOrder = 2,
            SortOrder = 131,
            TargetCount = 5,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.RelationshipFriendEstablished],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 150,
                SourcePoints = 15,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "friends-20",
            Title = "People Person",
            Summary = "Make 20 friends.",
            Icon = "users-round",
            SeriesIdentifier = "friend-count",
            SeriesTitle = "Friend Count",
            SeriesOrder = 3,
            SortOrder = 132,
            TargetCount = 20,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.RelationshipFriendEstablished],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 500,
                SourcePoints = 40,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "friends-50",
            Title = "Community Pillar",
            Summary = "Make 50 friends.",
            Icon = "heart-handshake",
            SeriesIdentifier = "friend-count",
            SeriesTitle = "Friend Count",
            SeriesOrder = 4,
            SortOrder = 133,
            TargetCount = 50,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.RelationshipFriendEstablished],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 1200,
                SourcePoints = 80,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.friends.50",
                    Label = "Community Pillar",
                    Caption = "Made 50 friends on the Solar Network.",
                },
            },
        },
        new()
        {
            Identifier = "friends-100",
            Title = "The Connector",
            Summary = "Make 100 friends.",
            Icon = "network",
            SeriesIdentifier = "friend-count",
            SeriesTitle = "Friend Count",
            SeriesOrder = 5,
            SortOrder = 134,
            TargetCount = 100,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.RelationshipFriendEstablished],
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 3000,
                SourcePoints = 150,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.friends.100",
                    Label = "The Connector",
                    Caption = "Made 100 friends on the Solar Network.",
                },
            },
        },
        new()
        {
            Identifier = "first-bookmark",
            Title = "Read Later",
            Summary = "Bookmark a post for the first time.",
            Icon = "bookmark",
            SortOrder = 113,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostBookmark] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 20,
                SourcePoints = 2,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "first-boost",
            Title = "Signal Boost",
            Summary = "Boost a post for the first time.",
            Icon = "repeat",
            SortOrder = 114,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostBoost] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 30,
                SourcePoints = 3,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "expert-reaction",
            Title = "Empath",
            Summary = "React to 1000 posts.",
            Icon = "heart",
            SortOrder = 202,
            TargetCount = 1000,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostReact] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 300,
                SourcePoints = 30,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.reaction.expert",
                    Label = "Empath",
                    Caption = "Reacted to 1000 posts on the Solar Network.",
                },
            },
        },
        new()
        {
            Identifier = "profile-complete",
            Title = "Fully Realized",
            Summary = "Complete your profile with name, bio, avatar, birthday, location, and pronouns.",
            Icon = "user-check",
            SortOrder = 115,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.AccountProfileComplete] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 10,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.account.profile_complete",
                    Label = "Fully Realized",
                    Caption = "Completed their profile.",
                },
            },
        },
        new()
        {
            Identifier = "first-2fa",
            Title = "Fort Knox",
            Summary = "Enable two-factor authentication.",
            Icon = "shield-check",
            SortOrder = 116,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.AuthFactorEnable] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 80,
                SourcePoints = 8,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "first-chatroom",
            Title = "Room Hopper",
            Summary = "Join a chatroom for the first time.",
            Icon = "door-open",
            SortOrder = 117,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatroomJoin] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 30,
                SourcePoints = 3,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "chatroom-10",
            Title = "Regular",
            Summary = "Join 10 different chatrooms.",
            Icon = "meeting",
            SortOrder = 203,
            TargetCount = 10,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatroomJoin] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 200,
                SourcePoints = 20,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "bookmark-10",
            Title = "Librarian",
            Summary = "Bookmark 10 posts.",
            Icon = "bookmark",
            SortOrder = 204,
            TargetCount = 10,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostBookmark] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 10,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "boost-50",
            Title = "Signal Tower",
            Summary = "Boost 50 posts.",
            Icon = "repeat",
            SortOrder = 205,
            TargetCount = 50,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostBoost] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 250,
                SourcePoints = 25,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.boost.50",
                    Label = "Signal Tower",
                    Caption = "Boosted 50 posts on the Solar Network.",
                },
            },
        },
        new()
        {
            Identifier = "realm-join-10",
            Title = "Globe Trotter",
            Summary = "Join 10 realms.",
            Icon = "globe",
            SortOrder = 206,
            TargetCount = 10,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.RealmJoin] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 300,
                SourcePoints = 30,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "streak-post-90",
            Title = "Quarterly Author",
            Summary = "Create posts for 90 days in a row.",
            Icon = "calendar-check",
            SeriesIdentifier = "post-streak",
            SeriesTitle = "Posting Streak",
            SeriesOrder = 4,
            SortOrder = 207,
            TargetCount = 90,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.PostCreate],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 8000,
                SourcePoints = 200,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.post.streak.90",
                    Label = "Quarterly Author",
                    Caption = "Created posts for 90 days in a row.",
                },
            },
        },
        new()
        {
            Identifier = "streak-post-365",
            Title = "Daily Devotee",
            Summary = "Create posts for 365 days in a row.",
            Icon = "calendar-heart",
            SeriesIdentifier = "post-streak",
            SeriesTitle = "Posting Streak",
            SeriesOrder = 5,
            SortOrder = 208,
            TargetCount = 365,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.PostCreate],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 36500,
                SourcePoints = 500,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.post.streak.365",
                    Label = "Daily Devotee",
                    Caption = "Created posts for 365 days in a row.",
                },
            },
        },
        new()
        {
            Identifier = "streak-login-90",
            Title = "Perennial",
            Summary = "Stay active for 90 days in a row.",
            Icon = "sunrise",
            SeriesIdentifier = "activity-streak",
            SeriesTitle = "Activity Streak",
            SeriesOrder = 4,
            SortOrder = 211,
            TargetCount = 90,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.AccountActive],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 5000,
                SourcePoints = 120,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.login.streak.90",
                    Label = "Perennial",
                    Caption = "Stayed active for 90 days in a row.",
                },
            },
        },
        new()
        {
            Identifier = "hidden-night-owl",
            Title = "Night Owl",
            Summary = "Create a post between midnight and 4am.",
            Icon = "moon",
            Hidden = true,
            SortOrder = 900,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.hidden.night_owl",
                    Label = "Night Owl",
                    Caption = "Posted between midnight and 4am.",
                },
            },
        },
        new()
        {
            Identifier = "hidden-speed-friend",
            Title = "Instant Connection",
            Summary = "Accept a friend request within 60 seconds.",
            Icon = "zap",
            Hidden = true,
            SortOrder = 901,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.RelationshipFriendAccept] },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.hidden.speed_friend",
                    Label = "Instant Connection",
                    Caption = "Accepted a friend request within 60 seconds.",
                },
            },
        },
        new()
        {
            Identifier = "hidden-social-butterfly-day",
            Title = "Social Butterfly (Day)",
            Summary = "Make 5 friends in a single day.",
            Icon = "butterfly",
            Hidden = true,
            SortOrder = 902,
            TargetCount = 5,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.RelationshipFriendEstablished],
                Mode = ProgressionTriggerMode.Streak,
            },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 200,
                SourcePoints = 20,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
                Badge = new SnProgressBadgeRewardDefinition
                {
                    Type = "progression.hidden.social_butterfly_day",
                    Label = "Social Butterfly",
                    Caption = "Made 5 friends in a single day.",
                },
            },
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
            {
                Experience = 25,
                SourcePoints = 2,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "daily-react",
            Title = "Give Credit",
            Summary = "React to 3 posts today.",
            Icon = "heart",
            SortOrder = 11,
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostReact] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Daily },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 15,
                SourcePoints = 2,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "daily-chat",
            Title = "Say Something",
            Summary = "Send a chat message today.",
            Icon = "message-circle",
            SortOrder = 12,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.ChatUse] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Daily },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 10,
                SourcePoints = 1,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "weekly-post-5",
            Title = "Weekly Writer",
            Summary = "Create 5 posts this week.",
            Icon = "pen-line",
            SortOrder = 20,
            TargetCount = 5,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Weekly },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 10,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "weekly-react-20",
            Title = "Appreciator",
            Summary = "React to 20 posts this week.",
            Icon = "sparkles",
            SortOrder = 21,
            TargetCount = 20,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostReact] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Weekly },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 80,
                SourcePoints = 8,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "weekly-friend-1",
            Title = "Make a Friend",
            Summary = "Make a new friend this week.",
            Icon = "user-plus",
            SortOrder = 22,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition
            {
                Actions = [ActionLogType.RelationshipFriendEstablished],
            },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Weekly },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 50,
                SourcePoints = 5,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "weekly-boost-3",
            Title = "Amplifier",
            Summary = "Boost 3 posts this week.",
            Icon = "repeat",
            SortOrder = 23,
            TargetCount = 3,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostBoost] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Weekly },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 60,
                SourcePoints = 6,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "monthly-post-20",
            Title = "Chronicler",
            Summary = "Create 20 posts this month.",
            Icon = "book-open",
            SortOrder = 30,
            TargetCount = 20,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.PostCreate] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Monthly },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 300,
                SourcePoints = 30,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
        new()
        {
            Identifier = "monthly-realm-join",
            Title = "Explorer",
            Summary = "Join a new realm this month.",
            Icon = "compass",
            SortOrder = 31,
            TargetCount = 1,
            Trigger = new SnProgressTriggerDefinition { Actions = [ActionLogType.RealmJoin] },
            Schedule = new SnQuestScheduleConfig { Repeatability = QuestRepeatability.Monthly },
            Reward = new SnProgressRewardDefinition
            {
                Experience = 100,
                SourcePoints = 10,
                SourcePointsCurrency = WalletCurrency.SourcePoint,
            },
        },
    ];
}
