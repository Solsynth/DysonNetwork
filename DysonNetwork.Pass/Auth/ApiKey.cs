using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Pass.Auth;

public class ApiKey : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(1024)] public string Label { get; set; } = null!;
    
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;
    public Guid SessionId { get; set; }
    public AuthSession Session { get; set; } = null!;
    
    [NotMapped] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Key { get; set; }
}