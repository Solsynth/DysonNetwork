using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Models;

public enum DomainVerificationStatus
{
    Pending,      // Added, awaiting .well-known check
    Verified,     // .well-known file confirmed
    Failed,       // Verification attempt failed
    Revoked,      // Manually revoked
}

[Index(nameof(PublisherId), nameof(Domain), IsUnique = true)]
public class SnPublisherVerifiedDomain : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublisherId { get; set; }
    public SnPublisher Publisher { get; set; } = null!;

    [MaxLength(512)]
    public string Domain { get; set; } = string.Empty;

    public DomainVerificationStatus Status { get; set; } = DomainVerificationStatus.Pending;
    public Instant? VerifiedAt { get; set; }
    public Instant? LastCheckedAt { get; set; }
    public int FailedAttempts { get; set; } = 0;
    [MaxLength(4096)] public string? LastError { get; set; }
}
