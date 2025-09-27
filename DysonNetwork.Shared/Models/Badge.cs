using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public class SnAccountBadge : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Type { get; set; } = null!;
    [MaxLength(1024)] public string? Label { get; set; }
    [MaxLength(4096)] public string? Caption { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Meta { get; set; } = new();
    public Instant? ActivatedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    public SnAccountBadgeRef ToReference()
    {
        return new SnAccountBadgeRef
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

    public AccountBadge ToProtoValue()
    {
        var proto = new AccountBadge
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
    
    public static SnAccountBadge FromProtoValue(AccountBadge proto)
    {
        var badge = new SnAccountBadge
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

public class SnAccountBadgeRef : ModelBase
{
    public Guid Id { get; set; }
    public string Type { get; set; } = null!;
    public string? Label { get; set; }
    public string? Caption { get; set; }
    public Dictionary<string, object?> Meta { get; set; } = new();
    public Instant? ActivatedAt { get; set; }
    public Instant? ExpiredAt { get; set; }
    public Guid AccountId { get; set; }

    public BadgeReferenceObject ToProtoValue()
    {
        var proto = new BadgeReferenceObject
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
    
    
    public static SnAccountBadgeRef FromProtoValue(BadgeReferenceObject proto)
    {
        var badge = new SnAccountBadgeRef
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