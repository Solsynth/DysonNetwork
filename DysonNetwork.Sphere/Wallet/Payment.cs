using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Developer;
using NodaTime;

namespace DysonNetwork.Sphere.Wallet;

public class WalletCurrency
{
    public const string SourcePoint = "points";
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
    public decimal Amount { get; set; }
    public Instant ExpiredAt { get; set; }
    
    public Guid PayeeWalletId { get; set; }
    public Wallet PayeeWallet { get; set; } = null!;
    public Guid? TransactionId { get; set; }
    public Transaction? Transaction { get; set; }
    public Guid? IssuerAppId { get; set; }
    public CustomApp? IssuerApp { get; set; }
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