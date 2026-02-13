using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnChatMessage : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(4096)] public string? Content { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    [Column(TypeName = "jsonb")] public List<Guid>? MembersMentioned { get; set; }
    [MaxLength(36)] public string Nonce { get; set; } = null!;
    public Instant? EditedAt { get; set; }
    
    [Column(TypeName = "jsonb")] public List<SnCloudFileReferenceObject> Attachments { get; set; } = []; 

    [NotMapped]
    public Dictionary<string, int> ReactionsCount { get; set; } = new();

    [NotMapped]
    public Dictionary<string, bool>? ReactionsMade { get; set; }

    public List<SnChatReaction> Reactions { get; set; } = new();

    public Guid? RepliedMessageId { get; set; }
    public SnChatMessage? RepliedMessage { get; set; }
    public Guid? ForwardedMessageId { get; set; }
    public SnChatMessage? ForwardedMessage { get; set; }

    public Guid SenderId { get; set; }
    public SnChatMember Sender { get; set; } = null!;
    public Guid ChatRoomId { get; set; }
    [JsonIgnore] public SnChatRoom ChatRoom { get; set; } = null!;

    public string ResourceIdentifier => $"message:{Id}";

    /// <summary>
    /// Creates a shallow clone of this message for sync operations
    /// </summary>
    /// <returns>A new Message instance with copied properties</returns>
    public SnChatMessage Clone()
    {
        return new SnChatMessage
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

public class SnChatReaction : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MessageId { get; set; }
    [JsonIgnore] public SnChatMessage Message { get; set; } = null!;
    public Guid SenderId { get; set; }
    public SnChatMember Sender { get; set; } = null!;

    [MaxLength(256)] public string Symbol { get; set; } = null!;
    public MessageReactionAttitude Attitude { get; set; }
}

public class ChatReaction
{
    public string Id { get; set; } = null!;
    public string Symbol { get; set; } = null!;
    public int Attitude { get; set; }
    public string MessageId { get; set; } = null!;
    public string SenderId { get; set; } = null!;
    public Account? Sender { get; set; }
}
