using NodaTime;

namespace DysonNetwork.Shared.Stream;

public class WebSocketPacketEvent
{
    public static string Type => "websocket_msg";

    public const string SubjectPrefix = "websocket_";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public byte[] PacketBytes { get; set; } = null!;
}

public class WebSocketConnectedEvent
{
    public static string Type => "websocket_connected";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public Instant ConnectedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public bool IsOffline { get; set; } = false;
}

public class WebSocketDisconnectedEvent
{
    public static string Type => "websocket_disconnected";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public Instant DisconnectedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();
    public bool IsOffline { get; set; }
}
