using System.ComponentModel.DataAnnotations;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public enum SocialCreditRecordStatus
{
    Active,
    Expired
}

public class SnSocialCreditRecord : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string ReasonType { get; set; } = string.Empty;
    [MaxLength(1024)] public string Reason { get; set; } = string.Empty;
    public double Delta { get; set; }
    public SocialCreditRecordStatus Status { get; set; } = SocialCreditRecordStatus.Active;
    public Instant? ExpiredAt { get; set; }
    
    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;

    public Proto.SocialCreditRecord ToProto()
    {
        var proto = new Proto.SocialCreditRecord
        {
            Id = Id.ToString(),
            ReasonType = ReasonType,
            Reason = Reason,
            Delta = Delta,
            AccountId = AccountId.ToString(),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        return proto;
    }
}