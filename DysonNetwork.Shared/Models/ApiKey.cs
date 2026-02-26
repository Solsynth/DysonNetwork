using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public class SnApiKey : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Label { get; set; } = null!;

    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;
    public Guid SessionId { get; set; }
    public SnAuthSession Session { get; set; } = null!;

    [NotMapped]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key { get; set; }

    public DyApiKey ToProtoValue()
    {
        return new DyApiKey
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

    public static SnApiKey FromProtoValue(DyApiKey proto)
    {
        return new SnApiKey
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