using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

[Index(nameof(DeviceLibraryIdentifier), nameof(PassId), IsUnique = true)]
public class SnApplePassRegistration : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PassId { get; set; }
    [MaxLength(255)] public string DeviceLibraryIdentifier { get; set; } = string.Empty;
    [MaxLength(512)] public string PushToken { get; set; } = string.Empty;

    public SnApplePass Pass { get; set; } = null!;
}
