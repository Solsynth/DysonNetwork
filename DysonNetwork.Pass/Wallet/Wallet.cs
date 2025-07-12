using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Pass.Wallet;

public class Wallet : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public ICollection<WalletPocket> Pockets { get; set; } = new List<WalletPocket>();
    
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;
}

public class WalletPocket : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal Amount { get; set; }
    
    public Guid WalletId { get; set; }
    [JsonIgnore] public Wallet Wallet { get; set; } = null!;
}