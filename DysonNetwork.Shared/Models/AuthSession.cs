using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum SessionType
{
    Login,
    OAuth, // Trying to authorize other platforms
    Oidc, // Trying to connect other platforms
    ApiKey // API key session for bot/automated access
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
    [Column(TypeName = "jsonb")] public GeoPoint? Location { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    // The client device for this session
    public Guid? ClientId { get; set; }
    public SnAuthClient? Client { get; set; } = null!;
    
    // For sub-sessions (e.g. OAuth)
    public Guid? ParentSessionId { get; set; }
    public SnAuthSession? ParentSession { get; set; }

    // Computed: number of child sessions (not mapped to database)
    [NotMapped] public int ChildrenCount { get; set; }

    // The origin challenge for this session
    public Guid? ChallengeId { get; set; }

    // Indicates the session is for an OIDC connection
    public Guid? AppId { get; set; }

    public DyAuthSession ToProtoValue()
    {
        var proto = new DyAuthSession
        {
            Id = Id.ToString(),
            LastGrantedAt = LastGrantedAt?.ToTimestamp(),
            Type = Type switch
            {
                SessionType.Login => DySessionType.DyLogin,
                SessionType.OAuth => DySessionType.DyOauth,
                SessionType.Oidc => DySessionType.DyOidc,
                SessionType.ApiKey => DySessionType.DyApiKey,
                _ => DySessionType.DyChallengeTypeUnspecified
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

    public static SnAuthSession FromProtoValue(DyAuthSession proto)
    {
        var session = new SnAuthSession
        {
            Id = Guid.Parse(proto.Id),
            LastGrantedAt = proto.LastGrantedAt?.ToInstant(),
            Type = proto.Type switch
            {
                DySessionType.DyLogin => SessionType.Login,
                DySessionType.DyOauth => SessionType.OAuth,
                DySessionType.DyOidc => SessionType.Oidc,
                DySessionType.DyApiKey => SessionType.ApiKey,
                _ => SessionType.Login
            },
            IpAddress = proto.IpAddress,
            UserAgent = proto.UserAgent,
            ExpiredAt = proto.ExpiredAt?.ToInstant(),
            AccountId = Guid.Parse(proto.AccountId),
            ClientId = string.IsNullOrEmpty(proto.ClientId) ? null : Guid.Parse(proto.ClientId),
            ParentSessionId = string.IsNullOrEmpty(proto.ParentSessionId) ? null : Guid.Parse(proto.ParentSessionId),
            AppId = string.IsNullOrEmpty(proto.AppId) ? null : Guid.Parse(proto.AppId),
            Audiences = proto.Audiences.ToList(),
            Scopes = proto.Scopes.ToList()
        };

        if (proto.Account != null)
        {
            session.Account = SnAccount.FromProtoValue(proto.Account);
        }
        if (proto.Client != null)
        {
            session.Client = SnAuthClient.FromProtoValue(proto.Client);
        }

        return session;
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

    public DyAuthChallenge ToProtoValue() => new()
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
    
    public DyAuthClient ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Platform = (DyClientPlatform)Platform,
        DeviceName = DeviceName,
        DeviceLabel = DeviceLabel,
        DeviceId = DeviceId,
        AccountId = AccountId.ToString()
    };

    public static SnAuthClient FromProtoValue(DyAuthClient proto)
    {
        return new SnAuthClient
        {
            Id = Guid.Parse(proto.Id),
            Platform = (ClientPlatform)proto.Platform,
            DeviceName = proto.DeviceName,
            DeviceLabel = proto.DeviceLabel,
            DeviceId = proto.DeviceId,
            AccountId = Guid.Parse(proto.AccountId)
        };
    }
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
