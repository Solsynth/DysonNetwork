namespace DysonNetwork.Shared.Data;

public abstract class WebSocketPacketType
{
    public const string Error = "error";
    public const string MessageNew = "messages.new";
    public const string MessageUpdate = "messages.update";
    public const string MessageDelete = "messages.delete";
    public const string CallParticipantsUpdate = "call.participants.update";
}

