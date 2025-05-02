using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net.Mail;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class Message : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(4096)] public string Content { get; set; } = string.Empty;
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public List<Guid>? MembersMetioned { get; set; }
    public Instant? EditedAt { get; set; }

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
    public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
    public ICollection<MessageStatus> Statuses { get; set; } = new List<MessageStatus>();

    public Guid? RepliedMessageId { get; set; }
    public Message? RepliedMessage { get; set; }
    public Guid? ForwardedMessageId { get; set; }
    public Message? ForwardedMessage { get; set; }

    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;
    public long ChatRoomId { get; set; }
    [JsonIgnore] public ChatRoom ChatRoom { get; set; } = null!;
}

public enum MessageReactionAttitude
{
    Positive,
    Neutral,
    Negative,
}

public class MessageReaction : ModelBase
{
    public Guid MessageId { get; set; }
    [JsonIgnore] public Message Message { get; set; } = null!;
    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;

    [MaxLength(256)] public string Symbol { get; set; } = null!;
    public MessageReactionAttitude Attitude { get; set; }
}

/// If the status is exist, means the user has read the message.
public class MessageStatus : ModelBase
{
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;

    public Instant ReadAt { get; set; }
}

[NotMapped]
public class MessageStatusResponse
{
    public Guid MessageId { get; set; }
    public bool IsRead { get; set; }
}