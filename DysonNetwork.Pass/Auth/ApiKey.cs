using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Auth;

public class ApiKey : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Label { get; set; } = null!;

    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;
    public Guid SessionId { get; set; }
    public AuthSession Session { get; set; } = null!;

    [NotMapped]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key { get; set; }

    public DysonNetwork.Shared.Proto.ApiKey ToProtoValue()
    {
        return new DysonNetwork.Shared.Proto.ApiKey
        {
            Id = Id.ToString(),
            Label = Label,
            AccountId = AccountId.ToString(),
            SessionId = SessionId.ToString(),
            Key = Key,
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };
    }

    public static ApiKey FromProtoValue(DysonNetwork.Shared.Proto.ApiKey proto)
    {
        return new ApiKey
        {
            Id = Guid.Parse(proto.Id),
            AccountId = Guid.Parse(proto.AccountId),
            SessionId = Guid.Parse(proto.SessionId),
            Label = proto.Label,
            Key = proto.Key,
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };
    }
}