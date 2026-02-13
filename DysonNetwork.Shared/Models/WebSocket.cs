namespace DysonNetwork.Shared.Models;

public abstract class WebSocketPacketType
{
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
    public const string MessageNew = "messages.new";
    public const string MessageUpdate = "messages.update";
    public const string MessageDelete = "messages.delete";
    public const string MessageReactionAdded = "messages.reaction.added";
    public const string MessageReactionRemoved = "messages.reaction.removed";
    public const string CallParticipantsUpdate = "call.participants.update";
}

