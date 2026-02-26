using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Proto;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public class SnExperienceRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string ReasonType { get; set; } = string.Empty;
    [MaxLength(1024)] public string Reason { get; set; } = string.Empty;
    public long Delta { get; set; }
    public double BonusMultiplier { get; set; }
    
    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;

    public DyExperienceRecord ToProto()
    {
        var proto = new DyExperienceRecord
        {
            Id = Id.ToString(),
            ReasonType = ReasonType,
            Reason = Reason,
            Delta = Delta,
            BonusMultiplier = BonusMultiplier,
            AccountId = AccountId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        return proto;
    }
}