using System.ComponentModel.DataAnnotations;
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
    public long Id { get; set; }
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;
    public ChatRoomType Type { get; set; }
    public bool IsPublic { get; set; }

    public string? PictureId { get; set; }
    public CloudFile? Picture { get; set; }
    public string? BackgroundId { get; set; }
    public CloudFile? Background { get; set; }

    [JsonIgnore] public ICollection<ChatMember> Members { get; set; } = new List<ChatMember>();

    public long? RealmId { get; set; }
    public Realm.Realm? Realm { get; set; }
}

public enum ChatMemberRole
{
    Owner = 100,
    Moderator = 50,
    Normal = 0
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
    public long ChatRoomId { get; set; }
    public ChatRoom ChatRoom { get; set; } = null!;
    public long AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    [MaxLength(1024)] public string? Nick { get; set; }

    public ChatMemberRole Role { get; set; } = ChatMemberRole.Normal;
    public ChatMemberNotify Notify { get; set; } = ChatMemberNotify.All;
    public Instant? JoinedAt { get; set; }
    public bool IsBot { get; set; } = false;
}