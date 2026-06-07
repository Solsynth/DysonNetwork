using DysonNetwork.Shared.EventBus;
using NodaTime;

namespace DysonNetwork.Shared.Queue;

/// <summary>
/// Event published when a bot's chat configuration is updated.
/// This allows other services to invalidate their caches.
/// </summary>
public class BotChatConfigUpdatedEvent : EventBase
{
    public override string EventType => "bot.chat.config.updated";
    public override string StreamName => "bot_chat_events";
    
    /// <summary>
    /// The bot account ID (AutomatedId) whose config was updated.
    /// </summary>
    public Guid BotAccountId { get; set; }
    
    /// <summary>
    /// When the config was updated.
    /// </summary>
    public Instant UpdatedAt { get; set; }
}
