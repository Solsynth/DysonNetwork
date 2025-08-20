using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Pass.Credit;

public class SocialCreditRecord : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(1024)] public string ReasonType { get; set; } = string.Empty;
    [MaxLength(1024)] public string Reason { get; set; } = string.Empty;
    public double Delta { get; set; }
    public Instant? ExpiredAt { get; set; }
    
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;
}