using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Messager.Chat.Voice;

public class SnChatVoiceClip : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChatRoomId { get; set; }
    public SnChatRoom ChatRoom { get; set; } = null!;

    public Guid SenderId { get; set; }
    public SnChatMember Sender { get; set; } = null!;

    [MaxLength(128)] public string MimeType { get; set; } = "audio/webm";
    [MaxLength(512)] public string StoragePath { get; set; } = null!;
    [MaxLength(256)] public string OriginalFileName { get; set; } = string.Empty;

    public long Size { get; set; }
    public int? DurationMs { get; set; }
}
