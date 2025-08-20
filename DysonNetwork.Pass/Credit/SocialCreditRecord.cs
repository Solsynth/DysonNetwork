using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Pass.Credit;

using Google.Protobuf.WellKnownTypes;

public class SocialCreditRecord : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string ReasonType { get; set; } = string.Empty;
    [MaxLength(1024)] public string Reason { get; set; } = string.Empty;
    public double Delta { get; set; }
    public Instant? ExpiredAt { get; set; }
    
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    public Shared.Proto.SocialCreditRecord ToProto()
    {
        var proto = new Shared.Proto.SocialCreditRecord
        {
            Id = Id.ToString(),
            ReasonType = ReasonType,
            Reason = Reason,
            Delta = Delta,
            AccountId = AccountId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset())
        };

        return proto;
    }
}