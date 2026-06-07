using DysonNetwork.Shared.EventBus;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

/// <summary>
/// Event published when a new chat message is sent to a bot account.
/// This event is consumed by webhook delivery services to notify bot owners.
/// </summary>
public class BotChatMessageEvent : EventBase
{
    public override string EventType => "bot.chat.message";
    public override string StreamName => "bot_chat_events";
    
    /// <summary>
    /// The bot account ID (AutomatedId) that should receive this event.
    /// </summary>
    public Guid BotAccountId { get; set; }
    
    /// <summary>
    /// The chat room where the message was sent.
    /// </summary>
    public Guid RoomId { get; set; }
    
    /// <summary>
    /// The message ID.
    /// </summary>
    public Guid MessageId { get; set; }
    
    /// <summary>
    /// The account ID of the message sender.
    /// </summary>
    public Guid SenderAccountId { get; set; }
    
    /// <summary>
    /// The message content (null for encrypted messages).
    /// </summary>
    public string? Content { get; set; }
    
    /// <summary>
    /// The message type (e.g., "text", "voice", "system.member.joined").
    /// </summary>
    public string MessageType { get; set; } = "text";
    
    /// <summary>
    /// Additional message metadata.
    /// </summary>
    public Dictionary<string, object>? Meta { get; set; }
    
    /// <summary>
    /// When the message was created.
    /// </summary>
    public Instant CreatedAt { get; set; }
}
