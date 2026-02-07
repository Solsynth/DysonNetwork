using DysonNetwork.Shared.EventBus;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

public class WebSocketPacketEvent : EventBase
{
    public static string Type => "websocket_msg";
    public override string EventType => Type;
    public override string StreamName => "websocket_events";

    public const string SubjectPrefix = "websocket_";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public byte[] PacketBytes { get; set; } = null!;
}

public class WebSocketConnectedEvent : EventBase
{
    public static string Type => "websocket_connected";
    public override string EventType => Type;
    public override string StreamName => "websocket_events";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public Instant ConnectedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public bool IsOffline { get; set; } = false;
}

public class WebSocketDisconnectedEvent : EventBase
{
    public static string Type => "websocket_disconnected";
    public override string EventType => Type;
    public override string StreamName => "websocket_events";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public Instant DisconnectedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public bool IsOffline { get; set; }
}
