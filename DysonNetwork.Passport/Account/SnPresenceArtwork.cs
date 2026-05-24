using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public class SnPresenceArtwork : ModelBase
{
    [Key]
    [MaxLength(71)]
    public string Hash { get; set; } = null!;

    [MaxLength(255)]
    public string MimeType { get; set; } = null!;

    public long Size { get; set; }

    [MaxLength(1024)]
    public string StoragePath { get; set; } = null!;

    public Instant LastReferencedAt { get; set; }
}
