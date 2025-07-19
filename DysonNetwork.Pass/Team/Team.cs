using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Team;

public enum TeamType
{
    Individual,
    Organizational
}

[Index(nameof(Name), IsUnique = true)]
public class Team : ModelBase, IIdentifiedResource
{
    public Guid Id { get; set; }
    public TeamType Type { get; set; }
    [MaxLength(256)] public string Name { get; set; } = string.Empty;
    [MaxLength(256)] public string Nick { get; set; } = string.Empty;
    [MaxLength(4096)] public string? Bio { get; set; }

    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Picture { get; set; }
    [Column(TypeName = "jsonb")] public CloudFileReferenceObject? Background { get; set; }

    [Column(TypeName = "jsonb")] public VerificationMark? Verification { get; set; }

    [JsonIgnore] public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
    [JsonIgnore] public ICollection<TeamFeature> Features { get; set; } = new List<TeamFeature>();

    public Guid? AccountId { get; set; }
    public Account.Account? Account { get; set; }

    public string ResourceIdentifier => $"publisher/{Id}";
}

public enum TeamMemberRole
{
    Owner = 100,
    Manager = 75,
    Editor = 50,
    Viewer = 25
}

public class TeamMember : ModelBase
{
    public Guid TeamId { get; set; }
    [JsonIgnore] public Team Team { get; set; } = null!;
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    public TeamMemberRole Role { get; set; } = TeamMemberRole.Viewer;
    public Instant? JoinedAt { get; set; }
}

public enum TeamSubscriptionStatus
{
    Active,
    Expired,
    Cancelled
}

public class TeamFeature : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string Flag { get; set; } = null!;
    public Instant? ExpiredAt { get; set; }

    public Guid TeamId { get; set; }
    public Team Team { get; set; } = null!;
}

public abstract class TeamFeatureFlag
{
    public static List<string> AllFlags => [Develop];
    public static string Develop = "develop";
}