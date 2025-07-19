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
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Meta { get; set; } = new();
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
            AccountId = AccountId,
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
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };
        proto.Meta.Add(GrpcTypeHelper.ConvertToValueMap(Meta));

        return proto;
    }
    
    public static AccountBadge FromProtoValue(Shared.Proto.AccountBadge proto)
    {
        var badge = new AccountBadge
        {
            Id = Guid.Parse(proto.Id),
            AccountId = Guid.Parse(proto.AccountId),
            Type = proto.Type,
            Label = proto.Label,
            Caption = proto.Caption,
            ActivatedAt = proto.ActivatedAt?.ToInstant(),
            ExpiredAt = proto.ExpiredAt?.ToInstant(),
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };

        return badge;
    }
}

public class BadgeReferenceObject : ModelBase
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string? Label { get; set; }
    public string? Caption { get; set; }
    public Dictionary<string, object?> Meta { get; set; }
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
        proto.Meta.Add(GrpcTypeHelper.ConvertToValueMap(Meta!));

        return proto;
    }
    
    
    public static BadgeReferenceObject FromProtoValue(Shared.Proto.BadgeReferenceObject proto)
    {
        var badge = new BadgeReferenceObject
        {
            Id = Guid.Parse(proto.Id),
            Type = proto.Type,
            Label = proto.Label,
            Caption = proto.Caption,
            Meta = GrpcTypeHelper.ConvertFromValueMap(proto.Meta).ToDictionary(),
            ActivatedAt = proto.ActivatedAt?.ToInstant(),
            ExpiredAt = proto.ExpiredAt?.ToInstant(),
            AccountId = Guid.Parse(proto.AccountId)
        };
        
        return badge;
    }
}