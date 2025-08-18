using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
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

    public Shared.Proto.AuthSession ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Label = Label,
        LastGrantedAt = LastGrantedAt?.ToTimestamp(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        AccountId = AccountId.ToString(),
        Account = Account.ToProtoValue(),
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

public enum ClientPlatform
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
    public ChallengeType Type { get; set; } = ChallengeType.Login;
    [Column(TypeName = "jsonb")] public List<Guid> BlacklistFactors { get; set; } = new();
    [Column(TypeName = "jsonb")] public List<string> Audiences { get; set; } = new();
    [Column(TypeName = "jsonb")] public List<string> Scopes { get; set; } = new();
    [MaxLength(128)] public string? IpAddress { get; set; }
    [MaxLength(512)] public string? UserAgent { get; set; }
    [MaxLength(1024)] public string? Nonce { get; set; }
    public Point? Location { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;
    public Guid? ClientId { get; set; }
    public AuthClient? Client { get; set; } = null!;

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
        Type = (Shared.Proto.ChallengeType)Type,
        BlacklistFactors = { BlacklistFactors.Select(x => x.ToString()) },
        Audiences = { Audiences },
        Scopes = { Scopes },
        IpAddress = IpAddress,
        UserAgent = UserAgent,
        DeviceId = Client!.DeviceId,
        Nonce = Nonce,
        AccountId = AccountId.ToString()
    };
}

public class AuthClient : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClientPlatform Platform { get; set; } = ClientPlatform.Unidentified;
    [MaxLength(1024)] public string DeviceName { get; set; } = string.Empty;
    [MaxLength(1024)] public string? DeviceLabel { get; set; }
    [MaxLength(1024)] public string DeviceId { get; set; } = string.Empty;

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;
}

public class AuthClientWithChallenge : AuthClient
{
    public List<AuthChallenge> Challenges { get; set; } = [];

    public static AuthClientWithChallenge FromClient(AuthClient client)
    {
        return new AuthClientWithChallenge
        {
            Id = client.Id,
            Platform = client.Platform,
            DeviceName = client.DeviceName,
            DeviceLabel = client.DeviceLabel,
            DeviceId = client.DeviceId,
            AccountId = client.AccountId,
        };
    }
}
