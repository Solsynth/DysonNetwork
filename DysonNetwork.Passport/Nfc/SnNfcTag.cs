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
    /// Chip UID extracted from SUN decryption. Unique per physical tag.
    /// </summary>
    [MaxLength(32)]
    public string Uid { get; set; } = string.Empty;

    /// <summary>
    /// Owner account ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Per-tag SUN decryption key (AES-128, 16 bytes). Stored as raw bytes.
    /// Used to verify MAC and decrypt PICCData from SUN URL parameters.
    /// </summary>
    public byte[] SunKey { get; set; } = [];

    /// <summary>
    /// Read counter for replay protection. Each successful scan increments this.
    /// NTAG424 tags have a built-in read counter that increments on every read.
    /// </summary>
    public int Counter { get; set; }

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
}
