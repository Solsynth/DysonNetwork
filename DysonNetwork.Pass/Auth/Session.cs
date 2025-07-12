using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Pass;
using DysonNetwork.Pass.Developer;
using DysonNetwork.Shared.Data;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using Point = NetTopologySuite.Geometries.Point;

namespace DysonNetwork.Pass.Auth;

public class AuthSession : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string? Label { get; set; }
    public Instant? LastGrantedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;
    public Guid ChallengeId { get; set; }
    public AuthChallenge Challenge { get; set; } = null!;
    public Guid? AppId { get; set; }
    public CustomApp? App { get; set; }

    public Shared.Proto.AuthSession ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Label = Label,
        LastGrantedAt = LastGrantedAt?.ToTimestamp(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        AccountId = AccountId.ToString(),
        ChallengeId = ChallengeId.ToString(),
        Challenge = Challenge.ToProtoValue(),
        AppId = AppId?.ToString()
    };
}

public enum ChallengeType
{
    Login,
    OAuth, // Trying to authorize other platforms
    Oidc // Trying to connect other platforms
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

public class AuthChallenge : ModelBase
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

    public AuthChallenge Normalize()
    {
        if (StepRemain == 0 && BlacklistFactors.Count == 0) StepRemain = StepTotal;
        return this;
    }

    public Shared.Proto.AuthChallenge ToProtoValue() => new()
    {
        Id = Id.ToString(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        StepRemain = StepRemain,
        StepTotal = StepTotal,
        FailedAttempts = FailedAttempts,
        Platform = (Shared.Proto.ChallengePlatform)Platform,
        Type = (Shared.Proto.ChallengeType)Type,
        BlacklistFactors = { BlacklistFactors.Select(x => x.ToString()) },
        Audiences = { Audiences },
        Scopes = { Scopes },
        IpAddress = IpAddress,
        UserAgent = UserAgent,
        DeviceId = DeviceId,
        Nonce = Nonce,
        AccountId = AccountId.ToString()
    };
}