using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum PostInterestKind
{
    Tag,
    Category,
    Publisher,
}

[Index(nameof(AccountId), nameof(Kind), nameof(ReferenceId), nameof(DeletedAt), IsUnique = true)]
public class SnPostInterestProfile : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public PostInterestKind Kind { get; set; }
    public Guid ReferenceId { get; set; }
    public double Score { get; set; }
    public int InteractionCount { get; set; }
    public Instant? LastInteractedAt { get; set; }

    [MaxLength(64)]
    public string? LastSignalType { get; set; }
}
