using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using NodaTime;
using Account = DysonNetwork.Pass.Account.Account;

namespace DysonNetwork.Sphere.Chat;

public enum ChatRoomType
{
    Group,
    DirectMessage
}

public class ChatRoom : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string? Name { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    public ChatRoomType Type { get; set; }
    public bool IsCommunity { get; set; }
    public bool IsPublic { get; set; }

    // Outdated fields, for backward compability
    [MaxLength(32)] public string? PictureId { get; set; }
    [MaxLength(32)] public string? BackgroundId { get; set; }
    
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    [JsonIgnore] public ICollection<ChatMember> Members { get; set; } = new List<ChatMember>();

    public Guid? RealmId { get; set; }
    public Realm.Realm? Realm { get; set; }

    [NotMapped]
    [JsonPropertyName("members")]
    public ICollection<ChatMemberTransmissionObject> DirectMembers { get; set; } =
        new List<ChatMemberTransmissionObject>();

    public string ResourceIdentifier => $"chatroom:{Id}";
}

public abstract class ChatMemberRole
{
    public const int Owner = 100;
    public const int Moderator = 50;
    public const int Member = 0;
}

public enum ChatMemberNotify
{
    All,
    Mentions,
    None
}

public enum ChatTimeoutCauseType
{
    ByModerator = 0,
    BySlowMode = 1,
}

public class ChatTimeoutCause
{
    public ChatTimeoutCauseType Type { get; set; }
    public Guid? SenderId { get; set; }
}

public class ChatMember : ModelBase
{
    public Guid Id { get; set; }
    public Guid ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public Account? Account { get; set; }

    [MaxLength(1024)] public string? Nick { get; set; }

    public int Role { get; set; } = ChatMemberRole.Member;
    public ChatMemberNotify Notify { get; set; } = ChatMemberNotify.All;
    public Instant? LastReadAt { get; set; }
    public Instant? JoinedAt { get; set; }
    public Instant? LeaveAt { get; set; }
    public bool IsBot { get; set; } = false;
    
    /// <summary>
    /// The break time is the user doesn't receive any message from this member for a while.
    /// Expect mentioned him or her.
    /// </summary>
    public Instant? BreakUntil { get; set; }
    /// <summary>
    /// The timeout is the user can't send any message.
    /// Set by the moderator of the chat room.
    /// </summary>
    public Instant? TimeoutUntil { get; set; }
    /// <summary>
    /// The timeout cause is the reason why the user is timeout.
    /// </summary>
    [Column(TypeName = "jsonb")] public ChatTimeoutCause? TimeoutCause { get; set; }
}

public class ChatMemberTransmissionObject : ModelBase
{
    public Guid Id { get; set; }
    public Guid ChatRoomId { get; set; }
    public Guid AccountId { get; set; }
    [NotMapped] public Account Account { get; set; } = null!;

    [MaxLength(1024)] public string? Nick { get; set; }

    public int Role { get; set; } = ChatMemberRole.Member;
    public ChatMemberNotify Notify { get; set; } = ChatMemberNotify.All;
    public Instant? JoinedAt { get; set; }
    public Instant? LeaveAt { get; set; }
    public bool IsBot { get; set; } = false;
    
    public Instant? BreakUntil { get; set; }
    public Instant? TimeoutUntil { get; set; }
    public ChatTimeoutCause? TimeoutCause { get; set; }

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
            LeaveAt = member.LeaveAt,
            IsBot = member.IsBot,
            BreakUntil = member.BreakUntil,
            TimeoutUntil = member.TimeoutUntil,
            TimeoutCause = member.TimeoutCause,
            CreatedAt = member.CreatedAt,
            UpdatedAt = member.UpdatedAt,
            DeletedAt = member.DeletedAt
        };
    }
}