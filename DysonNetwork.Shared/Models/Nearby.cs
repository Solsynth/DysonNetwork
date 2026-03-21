using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum NearbyDeviceStatus
{
    Active = 0,
    Disabled = 1
}

[Index(nameof(UserId), nameof(DeviceId), IsUnique = true)]
public class SnNearbyDevice : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    [NotMapped] public SnAccount? User { get; set; }
    [MaxLength(256)] public string DeviceId { get; set; } = string.Empty;
    public bool Discoverable { get; set; } = true;
    public bool FriendOnly { get; set; } = true;
    public int Capabilities { get; set; }
    public NearbyDeviceStatus Status { get; set; } = NearbyDeviceStatus.Active;
    public Instant? LastHeartbeatAt { get; set; }
    public Instant? LastTokenIssuedAt { get; set; }
    [JsonIgnore] public List<SnNearbyPresenceToken> PresenceTokens { get; set; } = [];
}

[Index(nameof(DeviceId), nameof(Slot), IsUnique = true)]
[Index(nameof(TokenHash))]
public class SnNearbyPresenceToken : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DeviceId { get; set; }
    [JsonIgnore] public SnNearbyDevice Device { get; set; } = null!;
    public long Slot { get; set; }
    [MaxLength(128)] public string TokenHash { get; set; } = string.Empty;
    public Instant ValidFrom { get; set; }
    public Instant ValidTo { get; set; }
    public bool Discoverable { get; set; }
    public bool FriendOnly { get; set; }
    public int Capabilities { get; set; }
}
