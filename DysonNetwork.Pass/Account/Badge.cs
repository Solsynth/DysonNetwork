using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Account;

public class AccountBadge : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(1024)] public string? Label { get; set; }
    [MaxLength(4096)] public string? Caption { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Meta { get; set; } = new();
    public Instant? ActivatedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public Account Account { get; set; } = null!;

    public BadgeReferenceObject ToReference()
    {
        return new BadgeReferenceObject
        {
            Id = Id,
            Type = Type,
            Label = Label,
            Caption = Caption,
            Meta = Meta,
            ActivatedAt = ActivatedAt,
            ExpiredAt = ExpiredAt,
            AccountId = AccountId
        };
    }

    public Shared.Proto.AccountBadge ToProtoValue()
    {
        var proto = new Shared.Proto.AccountBadge
        {
            Id = Id.ToString(),
            Type = Type,
            Label = Label ?? string.Empty,
            Caption = Caption ?? string.Empty,
            ActivatedAt = ActivatedAt?.ToTimestamp(),
            ExpiredAt = ExpiredAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
        };
        proto.Meta.Add(GrpcTypeHelper.ConvertToValueMap(Meta));

        return proto;
    }
}

public class BadgeReferenceObject : ModelBase
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string? Label { get; set; }
    public string? Caption { get; set; }
    public Dictionary<string, object>? Meta { get; set; }
    public Instant? ActivatedAt { get; set; }
    public Instant? ExpiredAt { get; set; }
    public Guid AccountId { get; set; }

    public Shared.Proto.BadgeReferenceObject ToProtoValue()
    {
        var proto = new Shared.Proto.BadgeReferenceObject
        {
            Id = Id.ToString(),
            Type = Type,
            Label = Label ?? string.Empty,
            Caption = Caption ?? string.Empty,
            ActivatedAt = ActivatedAt?.ToTimestamp(),
            ExpiredAt = ExpiredAt?.ToTimestamp(),
            AccountId = AccountId.ToString()
        };
        if (Meta is not null)
            proto.Meta.Add(GrpcTypeHelper.ConvertToValueMap(Meta!));

        return proto;
    }
}