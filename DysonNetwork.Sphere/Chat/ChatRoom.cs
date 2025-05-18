using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public enum ChatRoomType
{
    Group,
    DirectMessage
}

public class ChatRoom : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string? Name { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    public ChatRoomType Type { get; set; }
    public bool IsCommunity { get; set; }
    public bool IsPublic { get; set; }

    [MaxLength(32)] public string? PictureId { get; set; }
    public CloudFile? Picture { get; set; }
    [MaxLength(32)] public string? BackgroundId { get; set; }
    public CloudFile? Background { get; set; }

    [JsonIgnore] public ICollection<ChatMember> Members { get; set; } = new List<ChatMember>();

    public Guid? RealmId { get; set; }
    public Realm.Realm? Realm { get; set; }

    [NotMapped]
    [JsonPropertyName("members")]
    public ICollection<ChatMemberTransmissionObject> DirectMembers { get; set; } =
        new List<ChatMemberTransmissionObject>();
}

public enum ChatMemberRole
{
    Owner = 100,
    Moderator = 50,
    Member = 0
}

public enum ChatMemberNotify
{
    All,
    Mentions,
    None
}

public class ChatMember : ModelBase
{
    public Guid Id { get; set; }
    public Guid ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    [MaxLength(1024)] public string? Nick { get; set; }

    public ChatMemberRole Role { get; set; } = ChatMemberRole.Member;
    public ChatMemberNotify Notify { get; set; } = ChatMemberNotify.All;
    public Instant? JoinedAt { get; set; }
    public bool IsBot { get; set; } = false;
}

public class ChatMemberTransmissionObject : ModelBase
{
    public Guid Id { get; set; }
    public Guid ChatRoomId { get; set; }
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    [MaxLength(1024)] public string? Nick { get; set; }

    public ChatMemberRole Role { get; set; } = ChatMemberRole.Member;
    public ChatMemberNotify Notify { get; set; } = ChatMemberNotify.All;
    public Instant? JoinedAt { get; set; }
    public bool IsBot { get; set; } = false;

    public static ChatMemberTransmissionObject FromEntity(ChatMember member)
    {
        return new ChatMemberTransmissionObject
        {
            Id = member.Id,
            ChatRoomId = member.ChatRoomId,
            AccountId = member.AccountId,
            Account = member.Account,
            Nick = member.Nick,
            Role = member.Role,
            Notify = member.Notify,
            JoinedAt = member.JoinedAt,
            IsBot = member.IsBot,
            CreatedAt = member.CreatedAt,
            UpdatedAt = member.UpdatedAt,
            DeletedAt = member.DeletedAt
        };
    }
}