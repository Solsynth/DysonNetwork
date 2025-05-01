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

    public CloudFile? Picture { get; set; }
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

public class ChatMember : ModelBase
{
    public long ChatRoomId { get; set; }
    [JsonIgnore] public ChatRoom ChatRoom { get; set; } = null!;
    public long AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;

    public ChatMemberRole Role { get; set; }
    public Instant? JoinedAt { get; set; }
    public bool IsBot { get; set; } = false;
}