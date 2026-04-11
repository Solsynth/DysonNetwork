using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum LocationPinStatus
{
    Active = 0,
    Offline = 1,
    Removed = 2
}

[Index(nameof(AccountId), nameof(Status))]
[Index(nameof(MeetId), nameof(Status))]
public class SnLocationPin : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }
    public Guid? MeetId { get; set; }
    [NotMapped] public SnMeet? Meet { get; set; }
    [MaxLength(256)] public string DeviceId { get; set; } = null!;
    [MaxLength(256)] public string? LocationName { get; set; }
    [MaxLength(1024)] public string? LocationAddress { get; set; }
    [JsonIgnore]
    [Column(TypeName = "geometry (Geometry,4326)")]
    public NetTopologySuite.Geometries.Geometry? Location { get; set; }
    [NotMapped] public string? LocationWkt => Location?.AsText();
    public LocationVisibility Visibility { get; set; } = LocationVisibility.Private;
    public LocationPinStatus Status { get; set; } = LocationPinStatus.Active;
    public Instant LastHeartbeatAt { get; set; }
    public Instant? ExpiresAt { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Metadata { get; set; } = [];
    public bool KeepOnDisconnect { get; set; } = true;

    public string ResourceIdentifier => $"locationpin:{Id}";
}
