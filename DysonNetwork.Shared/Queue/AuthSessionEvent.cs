using DysonNetwork.Shared.EventBus;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public class AuthSessionRevokedEvent : EventBase
{
    public static string Type => "auth.session.revoked";
    public override string EventType => Type;
    public override string StreamName => "auth_session_events";

    public Guid SessionId { get; set; }
    public Guid AccountId { get; set; }
    public Guid? ClientId { get; set; }
    public string? DeviceId { get; set; }
    public Instant RevokedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
}
