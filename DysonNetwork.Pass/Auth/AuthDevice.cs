using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Auth;

[Index(nameof(DeviceId), IsUnique = true)]
public class AuthDevice : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string DeviceName { get; set; } = string.Empty;
    [MaxLength(1024)] public string DeviceId { get; set; } = string.Empty;
    
    public Guid AccountId { get; set; }
    [JsonIgnore] public Account.Account Account { get; set; } = null!;
}