using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Pass.Leveling;

using Google.Protobuf.WellKnownTypes;

public class ExperienceRecord : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string ReasonType { get; set; } = string.Empty;
    [MaxLength(1024)] public string Reason { get; set; } = string.Empty;
    public long Delta { get; set; }
    public double BonusMultiplier { get; set; }
    
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    public Shared.Proto.ExperienceRecord ToProto()
    {
        var proto = new Shared.Proto.ExperienceRecord
        {
            Id = Id.ToString(),
            ReasonType = ReasonType,
            Reason = Reason,
            Delta = Delta,
            BonusMultiplier = BonusMultiplier,
            AccountId = AccountId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };

        return proto;
    }
}