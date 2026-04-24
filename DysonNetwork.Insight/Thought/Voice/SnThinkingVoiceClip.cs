using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Insight.Thought.Voice;

public class SnThinkingVoiceClip : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid? SequenceId { get; set; }

    [MaxLength(128)] public string MimeType { get; set; } = "audio/webm";
    [MaxLength(1024)] public string StoragePath { get; set; } = null!;
    [MaxLength(256)] public string OriginalFileName { get; set; } = string.Empty;
    [MaxLength(128)] public string AccessToken { get; set; } = null!;

    public long Size { get; set; }
    public int? DurationMs { get; set; }
    public Instant ExpiresAt { get; set; }
}
