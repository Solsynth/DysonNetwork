using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Nfc;

[Index(nameof(Uid), IsUnique = true)]
[Index(nameof(UserId))]
public class SnNfcTag : DysonNetwork.Shared.Models.ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Unique identifier stored on the physical NFC tag.
    /// For encrypted tags this is derived from PICCData; for plain tags it's the entry UUID.
    /// </summary>
    [MaxLength(64)]
    public string Uid { get; set; } = string.Empty;

    /// <summary>
    /// Owner account ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Optional user-facing label for this tag (e.g., "Work Card", "Personal").
    /// </summary>
    [MaxLength(64)]
    public string? Label { get; set; }

    /// <summary>
    /// Whether this tag is active. Disabled tags won't resolve.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When the tag was locked (prevents physical reprogramming).
    /// Null means the tag has not been locked yet.
    /// </summary>
    public Instant? LockedAt { get; set; }

    /// <summary>
    /// Last time this tag was successfully scanned.
    /// </summary>
    public Instant? LastSeenAt { get; set; }

    /// <summary>
    /// SUN key for tag verification (legacy, unused).
    /// Kept for DB compatibility only.
    /// </summary>
    public byte[]? SunKey { get; set; }

    /// <summary>
    /// Scan counter for replay protection (legacy, unused).
    /// Kept for DB compatibility only.
    /// </summary>
    public int? Counter { get; set; }
}
