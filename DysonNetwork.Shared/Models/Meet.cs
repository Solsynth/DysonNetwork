using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum MeetStatus
{
    Active = 0,
    Completed = 1,
    Expired = 2,
    Cancelled = 3
}

public enum LocationVisibility
{
    Public = 0,
    Private = 1,
    Unlisted = 2
}

[Obsolete("Use LocationVisibility instead")]
public enum MeetVisibility
{
    Public = 0,
    Private = 1,
    Unlisted = 2
}

[Index(nameof(HostId), nameof(Status))]
public class SnMeet : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HostId { get; set; }
    [NotMapped] public SnAccount? Host { get; set; }
    public MeetStatus Status { get; set; } = MeetStatus.Active;
    public LocationVisibility Visibility { get; set; } = LocationVisibility.Private;
    public Instant ExpiresAt { get; set; }
    public Instant? CompletedAt { get; set; }
    [MaxLength(8192)] public string? Notes { get; set; }
    [MaxLength(256)] public string? LocationName { get; set; }
    [MaxLength(1024)] public string? LocationAddress { get; set; }
    [Column(TypeName = "jsonb")] public SnCloudFileReferenceObject? Image { get; set; }
    [JsonIgnore]
    [Column(TypeName = "geometry (Geometry,4326)")]
    public NetTopologySuite.Geometries.Geometry? Location { get; set; }
    [NotMapped] public string? LocationWkt => Location?.AsText();
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Metadata { get; set; } = [];
    public List<SnMeetParticipant> Participants { get; set; } = [];
    [NotMapped] public List<SnLocationPin> Pins { get; set; } = [];

    [NotMapped]
    public bool IsFinal => Status is MeetStatus.Completed or MeetStatus.Expired or MeetStatus.Cancelled;

    public string ResourceIdentifier => $"meet:{Id}";
}

public class SnMeetParticipant : ModelBase
{
    public Guid MeetId { get; set; }
    [JsonIgnore] public SnMeet Meet { get; set; } = null!;
    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }
    public Instant JoinedAt { get; set; }
}
