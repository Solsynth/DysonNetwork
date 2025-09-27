using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Slug), IsUnique = true)]
public class SnFileBundle : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Slug { get; set; } = null!;
    [MaxLength(1024)] public string Name { get; set; } = null!;
    [MaxLength(8192)] public string? Description { get; set; }
    [MaxLength(256)] public string? Passcode { get; set; }

    public List<SnCloudFile> Files { get; set; } = new();

    public Instant? ExpiredAt { get; set; }

    public Guid AccountId { get; set; }

    public SnFileBundle HashPasscode()
    {
        if (string.IsNullOrEmpty(Passcode)) return this;
        Passcode = BCrypt.Net.BCrypt.HashPassword(Passcode);
        return this;
    }

    public bool VerifyPasscode(string? passcode)
    {
        if (string.IsNullOrEmpty(Passcode)) return true;
        if (string.IsNullOrEmpty(passcode)) return false;
        return BCrypt.Net.BCrypt.Verify(passcode, Passcode);
    }
}