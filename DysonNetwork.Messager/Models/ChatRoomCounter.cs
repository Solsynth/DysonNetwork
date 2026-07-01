namespace DysonNetwork.Messager.Models;

public class SnChatRoomCounter
{
    public Guid ChatRoomId { get; set; }
    public long LastSequence { get; set; }
}
