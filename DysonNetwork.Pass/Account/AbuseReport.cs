using System.ComponentModel.DataAnnotations;
using NodaTime;

namespace DysonNetwork.Pass.Account;

public enum AbuseReportType
{
    Copyright,
    Harassment,
    Impersonation,
    OffensiveContent,
    Spam,
    PrivacyViolation,
    IllegalContent,
    Other
}

public class AbuseReport : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(4096)] public string ResourceIdentifier { get; set; } = null!;
    public AbuseReportType Type { get; set; }
    [MaxLength(8192)] public string Reason { get; set; } = null!;

    public Instant? ResolvedAt { get; set; }
    [MaxLength(8192)] public string? Resolution { get; set; }
    
    public Guid AccountId { get; set; }
    public Account Account { get; set; } = null!;
}