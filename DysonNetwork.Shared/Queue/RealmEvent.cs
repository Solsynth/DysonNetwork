using DysonNetwork.Shared.EventBus;

namespace DysonNetwork.Shared.Queue;

public class RealmActivityEvent : EventBase
{
    public const string SubjectPrefix = "realm.activity.";

    public Guid RealmId { get; set; }
    public Guid AccountId { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string ReferenceId { get; set; } = string.Empty;
    public int Delta { get; set; }

    public override string EventType => $"{SubjectPrefix}{ActivityType}";
    public override string StreamName => "realm_activity_events";
}
