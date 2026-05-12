using DysonNetwork.Shared.EventBus;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public static class ProgressionActionLogRegistry
{
    public static readonly HashSet<string> TrackedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        Shared.Models.ActionLogType.AccountProfileUpdate,
        Shared.Models.ActionLogType.AuthFactorCreate,
        Shared.Models.ActionLogType.AuthFactorEnable,
        Shared.Models.ActionLogType.AuthFactorDisable,
        Shared.Models.ActionLogType.AuthFactorDelete,
        Shared.Models.ActionLogType.AuthFactorResetPassword,
        Shared.Models.ActionLogType.NewLogin,
        Shared.Models.ActionLogType.AccountActive,
        Shared.Models.ActionLogType.StellarSupportMonth,
        Shared.Models.ActionLogType.SessionRevoke,
        Shared.Models.ActionLogType.DeviceRevoke,
        Shared.Models.ActionLogType.DeviceRename,
        Shared.Models.ActionLogType.AuthorizedAppDeauthorize,
        Shared.Models.ActionLogType.RelationshipFriendRequest,
        Shared.Models.ActionLogType.RelationshipFriendAccept,
        Shared.Models.ActionLogType.RelationshipFriendEstablished,
        Shared.Models.ActionLogType.RelationshipBlock,
        Shared.Models.ActionLogType.RelationshipUnblock,
        Shared.Models.ActionLogType.AccountAvatar,
        Shared.Models.ActionLogType.AccountProfileComplete,
        Shared.Models.ActionLogType.AccountConnectionLink,
        Shared.Models.ActionLogType.AccountPushEnable,
        Shared.Models.ActionLogType.PostCreate,
        Shared.Models.ActionLogType.PostFeatured,
        Shared.Models.ActionLogType.PostReact,
        Shared.Models.ActionLogType.PostBookmark,
        Shared.Models.ActionLogType.PostBoost,
        Shared.Models.ActionLogType.ChatUse,
        Shared.Models.ActionLogType.ChatroomJoin,
        Shared.Models.ActionLogType.PublisherCreate,
        Shared.Models.ActionLogType.PublisherMemberJoin,
        Shared.Models.ActionLogType.RealmCreate,
        Shared.Models.ActionLogType.RealmJoin
    };

    public static bool ShouldPublish(string action) => TrackedActions.Contains(action);
}

public class ActionLogTriggeredEvent : EventBase
{
    public const string SubjectPrefix = "action_logs.";

    public Guid ActionLogId { get; set; }
    public Guid AccountId { get; set; }
    public string Action { get; set; } = string.Empty;
    public Dictionary<string, object> Meta { get; set; } = new();
    public Guid? SessionId { get; set; }
    public Instant OccurredAt { get; set; } = SystemClock.Instance.GetCurrentInstant();

    public override string EventType => GetSubject(Action);
    public override string StreamName => "action_log_events";

    public static string GetSubject(string action) => $"{SubjectPrefix}{action}";
}
