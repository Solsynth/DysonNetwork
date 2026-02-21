using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnLiveStreamChatMessage : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LiveStreamId { get; set; }
    [JsonIgnore] public SnLiveStream? LiveStream { get; set; }

    public Guid SenderId { get; set; }
    [NotMapped] public SnAccount? Sender { get; set; }

    [MaxLength(128)] public string SenderName { get; set; } = null!;
    [MaxLength(4096)] public string Content { get; set; } = null!;

    public Instant? TimeoutUntil { get; set; }
}
