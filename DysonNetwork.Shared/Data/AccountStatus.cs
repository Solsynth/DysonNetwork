using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Data;

public class AccountStatusReference : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public StatusAttitude Attitude { get; set; }
    public bool IsOnline { get; set; }
    public bool IsCustomized { get; set; } = true;
    public bool IsInvisible { get; set; }
    public bool IsNotDisturb { get; set; }
    public string? Label { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
    public Instant? ClearedAt { get; set; }

    public Guid AccountId { get; set; }

    public AccountStatus ToProtoValue()
    {
        var proto = new AccountStatus
        {
            Id = Id.ToString(),
            Attitude = Attitude,
            IsOnline = IsOnline,
            IsCustomized = IsCustomized,
            IsInvisible = IsInvisible,
            IsNotDisturb = IsNotDisturb,
            Label = Label ?? string.Empty,
            Meta = GrpcTypeHelper.ConvertObjectToByteString(Meta),
            ClearedAt = ClearedAt?.ToTimestamp(),
            AccountId = AccountId.ToString()
        };

        return proto;
    }

    public static AccountStatusReference FromProtoValue(AccountStatus proto)
    {
        var status = new AccountStatusReference
        {
            Id = Guid.Parse(proto.Id),
            Attitude = proto.Attitude,
            IsOnline = proto.IsOnline,
            IsCustomized = proto.IsCustomized,
            IsInvisible = proto.IsInvisible,
            IsNotDisturb = proto.IsNotDisturb,
            Label = proto.Label,
            Meta = GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object>>(proto.Meta),
            ClearedAt = proto.ClearedAt?.ToInstant(),
            AccountId = Guid.Parse(proto.AccountId)
        };

        return status;
    }
}
