using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class Message : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(4096)] public string? Content { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public List<Guid>? MembersMentioned { get; set; }
    [MaxLength(36)] public string Nonce { get; set; } = null!;
    public Instant? EditedAt { get; set; }
    
    [Column(TypeName = "jsonb")] public List<CloudFileReferenceObject> Attachments { get; set; } = []; 

    public ICollection<MessageReaction> Reactions { get; set; } = new List<MessageReaction>();

    public Guid? RepliedMessageId { get; set; }
    public Message? RepliedMessage { get; set; }
    public Guid? ForwardedMessageId { get; set; }
    public Message? ForwardedMessage { get; set; }

    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;
    public Guid ChatRoomId { get; set; }
    [JsonIgnore] public ChatRoom ChatRoom { get; set; } = null!;

    public string ResourceIdentifier => $"message:{Id}";

    /// <summary>
    /// Creates a shallow clone of this message for sync operations
    /// </summary>
    /// <returns>A new Message instance with copied properties</returns>
    public Message Clone()
    {
        return new Message
        {
            Id = Id,
            Type = Type,
            Content = Content,
            Meta = Meta,
            MembersMentioned = MembersMentioned,
            Nonce = Nonce,
            EditedAt = EditedAt,
            Attachments = Attachments,
            RepliedMessageId = RepliedMessageId,
            ForwardedMessageId = ForwardedMessageId,
            SenderId = SenderId,
            Sender = Sender,
            ChatRoomId = ChatRoomId,
            ChatRoom = ChatRoom,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt
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
