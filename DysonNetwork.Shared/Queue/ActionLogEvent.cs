using DysonNetwork.Shared.EventBus;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public static class ProgressionActionLogRegistry
{
    public static readonly HashSet<string> TrackedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        Shared.Models.ActionLogType.PostCreate,
        Shared.Models.ActionLogType.PostReact,
        Shared.Models.ActionLogType.MessageCreate,
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
