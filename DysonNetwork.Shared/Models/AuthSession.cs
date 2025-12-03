using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.GeoIp;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum SessionType
{
    Login,
    OAuth, // Trying to authorize other platforms
    Oidc // Trying to connect other platforms
}

public class SnAuthSession : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SessionType Type { get; set; } = SessionType.Login;
    public Instant? LastGrantedAt { get; set; }
    public Instant? ExpiredAt { get; set; }
    
    [Column(TypeName = "jsonb")] public List<string> Audiences { get; set; } = [];
    [Column(TypeName = "jsonb")] public List<string> Scopes { get; set; } = [];
    [MaxLength(128)] public string? IpAddress { get; set; }
    [MaxLength(512)] public string? UserAgent { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    // The client device for this session
    public Guid? ClientId { get; set; }
    public SnAuthClient? Client { get; set; } = null!;
    
    // For sub-sessions (e.g. OAuth)
    public Guid? ParentSessionId { get; set; }
    public SnAuthSession? ParentSession { get; set; }

    // The origin challenge for this session
    public Guid? ChallengeId { get; set; }

    // Indicates the session is for an OIDC connection
    public Guid? AppId { get; set; }

    public AuthSession ToProtoValue()
    {
        var proto = new AuthSession
        {
            Id = Id.ToString(),
            LastGrantedAt = LastGrantedAt?.ToTimestamp(),
            Type = Type switch
            {
                SessionType.Login => Proto.SessionType.Login,
                SessionType.OAuth => Proto.SessionType.Oauth,
                SessionType.Oidc => Proto.SessionType.Oidc,
                _ => Proto.SessionType.ChallengeTypeUnspecified
            },
            IpAddress = IpAddress,
            UserAgent = UserAgent,
            ExpiredAt = ExpiredAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
            Account = Account.ToProtoValue(),
            ClientId = ClientId.ToString(),
            Client = Client?.ToProtoValue(),
            ParentSessionId = ParentSessionId.ToString(),
            AppId = AppId?.ToString()
        };
        
        proto.Audiences.AddRange(Audiences);
        proto.Scopes.AddRange(Scopes);

        return proto;
    }
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
    [Column(TypeName = "jsonb")] public List<Guid> BlacklistFactors { get; set; } = [];
    [Column(TypeName = "jsonb")] public List<string> Audiences { get; set; } = [];
    [Column(TypeName = "jsonb")] public List<string> Scopes { get; set; } = [];
    [MaxLength(128)] public string? IpAddress { get; set; }
    [MaxLength(512)] public string? UserAgent { get; set; }
    [MaxLength(512)] public string DeviceId { get; set; } = null!;
    [MaxLength(1024)] public string? DeviceName { get; set; }
    public ClientPlatform Platform { get; set; }
    [MaxLength(1024)] public string? Nonce { get; set; }
    [Column(TypeName = "jsonb")] public GeoPoint? Location { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    public SnAuthChallenge Normalize()
    {
        if (StepRemain == 0 && BlacklistFactors.Count == 0) StepRemain = StepTotal;
        return this;
    }

    public AuthChallenge ToProtoValue() => new()
    {
        Id = Id.ToString(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        StepRemain = StepRemain,
        StepTotal = StepTotal,
        FailedAttempts = FailedAttempts,
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

public class SnAuthClient : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ClientPlatform Platform { get; set; } = ClientPlatform.Unidentified;
    [MaxLength(1024)] public string DeviceName { get; set; } = string.Empty;
    [MaxLength(1024)] public string? DeviceLabel { get; set; }
    [MaxLength(1024)] public string DeviceId { get; set; } = string.Empty;

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;
    
    public Proto.AuthClient ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Platform = (Proto.ClientPlatform)Platform,
        DeviceName = DeviceName,
        DeviceLabel = DeviceLabel,
        DeviceId = DeviceId,
        AccountId = AccountId.ToString()
    };
}

public class SnAuthClientWithSessions : SnAuthClient
{
    public List<SnAuthSession> Sessions { get; set; } = [];

    public static SnAuthClientWithSessions FromClient(SnAuthClient client)
    {
        return new SnAuthClientWithSessions
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
