using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class RealtimeCall : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Title { get; set; }
    public Instant? EndedAt { get; set; }
    
    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;
    public long RoomId { get; set; }
    public ChatRoom Room { get; set; } = null!;
}