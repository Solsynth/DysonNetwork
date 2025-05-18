using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class Message : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = null!;
    [MaxLength(4096)] public string? Content { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public List<Guid>? MembersMentioned { get; set; }
    [MaxLength(36)] public string Nonce { get; set; } = null!;
    public Instant? EditedAt { get; set; }

    public ICollection<CloudFile> Attachments { get; set; } = new List<CloudFile>();
    public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();
    public ICollection<MessageReadReceipt> Statuses { get; set; } = new List<MessageReadReceipt>();

    public Guid? RepliedMessageId { get; set; }
    public Message? RepliedMessage { get; set; }
    public Guid? ForwardedMessageId { get; set; }
    public Message? ForwardedMessage { get; set; }

    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;
    public Guid ChatRoomId { get; set; }
    [JsonIgnore] public ChatRoom ChatRoom { get; set; } = null!;
    
    public Message Clone()
    {
        return new Message
        {
            Id = Id,
            Content = Content,
            Meta = Meta?.ToDictionary(entry => entry.Key, entry => entry.Value),
            MembersMentioned = MembersMentioned?.ToList(),
            Nonce = Nonce,
            EditedAt = EditedAt,
            Attachments = new List<CloudFile>(Attachments),
            Reactions = new List<MessageReaction>(Reactions),
            Statuses = new List<MessageReadReceipt>(Statuses),
            RepliedMessageId = RepliedMessageId,
            RepliedMessage = RepliedMessage?.Clone() as Message,
            ForwardedMessageId = ForwardedMessageId,
            ForwardedMessage = ForwardedMessage?.Clone() as Message,
            SenderId = SenderId,
            Sender = Sender,
            ChatRoomId = ChatRoomId,
            ChatRoom = ChatRoom,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            DeletedAt = DeletedAt
        };
    }
}

public enum MessageReactionAttitude
{
    Positive,
    Neutral,
    Negative,
}

public class MessageReaction : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    [JsonIgnore] public Message Message { get; set; } = null!;
    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;

    [MaxLength(256)] public string Symbol { get; set; } = null!;
    public MessageReactionAttitude Attitude { get; set; }
}

/// If the status exists, means the user has read the message.
[Index(nameof(MessageId), nameof(SenderId), IsUnique = true)]
public class MessageReadReceipt : ModelBase
{
    public Guid MessageId { get; set; }
    public Message Message { get; set; } = null!;
    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;
}

[NotMapped]
public class MessageStatusResponse
{
    public Guid MessageId { get; set; }
    public bool IsRead { get; set; }
}