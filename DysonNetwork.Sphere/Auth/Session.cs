using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;
using Point = NetTopologySuite.Geometries.Point;

namespace DysonNetwork.Sphere.Auth;

public class Session : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string? Label { get; set; }
    public Instant? LastGrantedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    [JsonIgnore] public Account.Account Account { get; set; } = null!;
    [JsonIgnore] public Challenge Challenge { get; set; } = null!;
}

public enum ChallengeType
{
    Login,
    OAuth
}

public enum ChallengePlatform
{
    Unidentified,
    Web,
    Ios,
    Android,
    MacOs,
    Windows,
    Linux
}

public class Challenge : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Instant? ExpiredAt { get; set; }
    public int StepRemain { get; set; }
    public int StepTotal { get; set; }
    public int FailedAttempts { get; set; }
    public ChallengePlatform Platform { get; set; } = ChallengePlatform.Unidentified;
    public ChallengeType Type { get; set; } = ChallengeType.Login;
    [Column(TypeName = "jsonb")] public List<Guid> BlacklistFactors { get; set; } = new();
    [Column(TypeName = "jsonb")] public List<string> Audiences { get; set; } = new();
    [Column(TypeName = "jsonb")] public List<string> Scopes { get; set; } = new();
    [MaxLength(128)] public string? IpAddress { get; set; }
    [MaxLength(512)] public string? UserAgent { get; set; }
    [MaxLength(256)] public string? DeviceId { get; set; }
    [MaxLength(1024)] public string? Nonce { get; set; }
    public Point? Location { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;

    public Challenge Normalize()
    {
        if (StepRemain == 0 && BlacklistFactors.Count == 0) StepRemain = StepTotal;
        return this;
    }
}