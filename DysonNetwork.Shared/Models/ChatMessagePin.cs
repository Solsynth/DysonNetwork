using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnChatMessagePin : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    [JsonIgnore] public SnChatMessage Message { get; set; } = null!;
    public Guid ChatRoomId { get; set; }
    [JsonIgnore] public SnChatRoom ChatRoom { get; set; } = null!;
    public Guid PinnedByMemberId { get; set; }
    public SnChatMember PinnedBy { get; set; } = null!;
    public Instant? ExpiresAt { get; set; }
}
