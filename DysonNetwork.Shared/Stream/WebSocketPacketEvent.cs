namespace DysonNetwork.Shared.Stream;

public class WebSocketPacketEvent
{
    public static string Type => "websocket_msg";

    public static string SubjectPrefix = "websocket_";

    public Guid AccountId { get; set; }
    public string DeviceId { get; set; } = null!;
    public byte[] PacketBytes { get; set; } = null!;
}
