using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Data;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Develop.Identity;

public class BotAccount : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public Guid ProjectId { get; set; }
    public DevProject Project { get; set; } = null!;
    
    [NotMapped] public AccountReference? Account { get; set; }

    public Shared.Proto.BotAccount ToProtoValue()
    {
        var proto = new Shared.Proto.BotAccount
        {
            Slug = Slug,
            IsActive = IsActive,
            AutomatedId = Id.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        return proto;
    }

    public static BotAccount FromProto(Shared.Proto.BotAccount proto)
    {
        var botAccount = new BotAccount
        {
            Id = Guid.Parse(proto.AutomatedId),
            Slug = proto.Slug,
            IsActive = proto.IsActive,
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant()
        };

        return botAccount;
    }
}