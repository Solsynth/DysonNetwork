using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Proto;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public class SnBotAccount : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;

    public bool IsActive { get; set; } = true;

    public Guid ProjectId { get; set; }
    public SnDevProject Project { get; set; } = null!;
    
    [NotMapped] public SnAccount? Account { get; set; }
    
    /// <summary>
    /// This developer field is to serve the transparent info for user to know which developer
    /// published this robot. Not for relationships usage.
    /// </summary>
    [NotMapped] public SnDeveloper? Developer { get; set; }

    public DyBotAccount ToProtoValue()
    {
        var proto = new DyBotAccount
        {
            Slug = Slug,
            IsActive = IsActive,
            AutomatedId = Id.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        return proto;
    }

    public static SnBotAccount FromProto(DyBotAccount proto)
    {
        var botAccount = new SnBotAccount
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