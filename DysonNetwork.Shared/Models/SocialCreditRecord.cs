using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Proto;
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

    public double GetEffectiveDelta(Instant now)
    {
        if (ExpiredAt.HasValue && ExpiredAt <= now)
            return 0;
            
        if (!ExpiredAt.HasValue)
            return Delta;
            
        var totalDuration = ExpiredAt.Value - CreatedAt;
        if (totalDuration == Duration.Zero)
            return Delta;
            
        var elapsed = now - CreatedAt;
        var remainingRatio = 1.0 - (elapsed.TotalSeconds / totalDuration.TotalSeconds);
        return Delta * Math.Max(0, remainingRatio);
    }

    public DySocialCreditRecord ToProto()
    {
        var proto = new DySocialCreditRecord
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