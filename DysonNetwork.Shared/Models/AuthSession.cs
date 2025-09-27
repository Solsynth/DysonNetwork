using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.GeoIp;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public class SnAuthSession : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Instant? LastGrantedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    // When the challenge is null, indicates the session is for an API Key
    public Guid? ChallengeId { get; set; }
    public SnAuthChallenge? Challenge { get; set; } = null!;

    // Indicates the session is for an OIDC connection
    public Guid? AppId { get; set; }

    public Proto.AuthSession ToProtoValue() => new()
    {
        Id = Id.ToString(),
        LastGrantedAt = LastGrantedAt?.ToTimestamp(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        AccountId = AccountId.ToString(),
        Account = Account.ToProtoValue(),
        ChallengeId = ChallengeId.ToString(),
        Challenge = Challenge?.ToProtoValue(),
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

public class SnAuthChallenge : ModelBase
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
    [Column(TypeName = "jsonb")] public GeoPoint? Location { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;
    public Guid? ClientId { get; set; }
    public SnAuthClient? Client { get; set; } = null!;

    public SnAuthChallenge Normalize()
    {
        if (StepRemain == 0 && BlacklistFactors.Count == 0) StepRemain = StepTotal;
        return this;
    }

    public Proto.AuthChallenge ToProtoValue() => new()
    {
        Id = Id.ToString(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        StepRemain = StepRemain,
        StepTotal = StepTotal,
        FailedAttempts = FailedAttempts,
        Type = (Proto.ChallengeType)Type,
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

public class SnAuthClient : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClientPlatform Platform { get; set; } = ClientPlatform.Unidentified;
    [MaxLength(1024)] public string DeviceName { get; set; } = string.Empty;
    [MaxLength(1024)] public string? DeviceLabel { get; set; }
    [MaxLength(1024)] public string DeviceId { get; set; } = string.Empty;

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;
}

public class SnAuthClientWithChallenge : SnAuthClient
{
    public List<SnAuthChallenge> Challenges { get; set; } = [];

    public static SnAuthClientWithChallenge FromClient(SnAuthClient client)
    {
        return new SnAuthClientWithChallenge
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