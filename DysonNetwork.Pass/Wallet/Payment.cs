using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Pass.Wallet;

public class WalletCurrency
{
    public const string SourcePoint = "points";
    public const string GoldenPoint = "golds";
}

public enum OrderStatus
{
    Unpaid,
    Paid,
    Cancelled,
    Finished,
    Expired
}

public class Order : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public OrderStatus Status { get; set; } = OrderStatus.Unpaid;
    [MaxLength(128)] public string Currency { get; set; } = null!;
    [MaxLength(4096)] public string? Remarks { get; set; }
    [MaxLength(4096)] public string? AppIdentifier { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    public decimal Amount { get; set; }
    public Instant ExpiredAt { get; set; }
    
    public Guid? PayeeWalletId { get; set; }
    public Wallet? PayeeWallet { get; set; } = null!;
    public Guid? TransactionId { get; set; }
    public Transaction? Transaction { get; set; }
}

public enum TransactionType
{
    System,
    Transfer,
    Order
}

public class Transaction : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal Amount { get; set; }
    [MaxLength(4096)] public string? Remarks { get; set; }
    public TransactionType Type { get; set; }
    
    // When the payer is null, it's pay from the system
    public Guid? PayerWalletId { get; set; }
    public Wallet? PayerWallet { get; set; }
    // When the payee is null, it's pay for the system
    public Guid? PayeeWalletId { get; set; }
    public Wallet? PayeeWallet { get; set; }
}