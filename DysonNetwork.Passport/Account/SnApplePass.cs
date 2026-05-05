using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

[Index(nameof(AccountId), nameof(PassTypeIdentifier), IsUnique = true)]
[Index(nameof(PassTypeIdentifier), nameof(SerialNumber), IsUnique = true)]
public class SnApplePass : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    [MaxLength(255)] public string PassTypeIdentifier { get; set; } = string.Empty;
    [MaxLength(255)] public string SerialNumber { get; set; } = string.Empty;
    [MaxLength(255)] public string AuthenticationToken { get; set; } = string.Empty;
    [MaxLength(128)] public string LastUpdatedTag { get; set; } = string.Empty;

    public List<SnApplePassRegistration> Registrations { get; set; } = [];
}
