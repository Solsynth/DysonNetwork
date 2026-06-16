using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using DysonNetwork.Shared.Proto;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public abstract class WalletCurrency
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

public class SnWalletOrder : ModelBase
{
    public const string InternalAppIdentifier = "internal";

    public Guid Id { get; set; } = Guid.NewGuid();
    public OrderStatus Status { get; set; } = OrderStatus.Unpaid;
    [MaxLength(128)] public string Currency { get; set; } = null!;
    [MaxLength(4096)] public string? Remarks { get; set; }
    [MaxLength(4096)] public string? AppIdentifier { get; set; }
    [MaxLength(4096)] public string? ProductIdentifier { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object>? Meta { get; set; }
    public decimal Amount { get; set; }
    public Instant ExpiredAt { get; set; }

    public Guid? PayeeWalletId { get; set; }
    public SnWallet? PayeeWallet { get; set; } = null!;
    public Guid? TransactionId { get; set; }
    public SnWalletTransaction? Transaction { get; set; }

    public DyOrder ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Status = (DyOrderStatus)Status,
        Currency = Currency,
        Remarks = Remarks,
        AppIdentifier = AppIdentifier,
        ProductIdentifier = ProductIdentifier,
        Meta = Meta == null
            ? null
            : Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Meta)),
        Amount = Amount.ToString(CultureInfo.InvariantCulture),
        ExpiredAt = ExpiredAt.ToTimestamp(),
        PayeeWalletId = PayeeWalletId?.ToString(),
        TransactionId = TransactionId?.ToString(),
        Transaction = Transaction?.ToProtoValue(),
    };

    public static SnWalletOrder FromProtoValue(DyOrder proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Status = (OrderStatus)proto.Status,
        Currency = proto.Currency,
        Remarks = proto.Remarks,
        AppIdentifier = proto.AppIdentifier,
        ProductIdentifier = proto.ProductIdentifier,
        Meta = proto.HasMeta
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(proto.Meta.ToByteArray())
            : null,
        Amount = decimal.Parse(proto.Amount),
        ExpiredAt = proto.ExpiredAt.ToInstant(),
        PayeeWalletId = proto.PayeeWalletId is not null ? Guid.Parse(proto.PayeeWalletId) : null,
        TransactionId = proto.TransactionId is not null ? Guid.Parse(proto.TransactionId) : null,
        Transaction = proto.Transaction is not null ? SnWalletTransaction.FromProtoValue(proto.Transaction) : null,
    };
}

public enum TransactionType
{
    System,
    Transfer,
    Order
}

public enum WalletTransferRequestStatus
{
    Active,
    Fulfilled,
    Expired,
    Cancelled
}

public enum TransactionStatus
{
    Pending,      // Created, funds held from payer, not yet credited to payee
    Frozen,       // Funds held for 24hr settlement period
    Confirmed,    // Payee confirmed (or freeze period elapsed), funds released
    Refunded,     // Returned to payer (rejection or expiration)
    Cancelled     // Cancelled before funds were held
}

public class SnWalletTransaction : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal Amount { get; set; }
    [MaxLength(4096)] public string? Remarks { get; set; }
    public TransactionType Type { get; set; }

    // Transaction lifecycle status
    public TransactionStatus Status { get; set; } = TransactionStatus.Confirmed;
    public bool IsFrozen { get; set; } = false;
    public bool RequireConfirmation { get; set; } = false;
    public Instant? FrozenAt { get; set; }
    public Instant? ExpiresAt { get; set; }
    public Instant? ConfirmedAt { get; set; }

    // When the payer is null, it's pay from the system
    public Guid? PayerWalletId { get; set; }

    public SnWallet? PayerWallet { get; set; }

    // When the payee is null, it's pay for the system
    public Guid? PayeeWalletId { get; set; }
    public SnWallet? PayeeWallet { get; set; }

    public DyTransaction ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Currency = Currency,
        Amount = Amount.ToString(CultureInfo.InvariantCulture),
        Remarks = Remarks,
        Type = (DyTransactionType)Type,
        PayerWalletId = PayerWalletId?.ToString(),
        PayeeWalletId = PayeeWalletId?.ToString(),
    };

    public static SnWalletTransaction FromProtoValue(DyTransaction proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Currency = proto.Currency,
        Amount = decimal.Parse(proto.Amount),
        Remarks = proto.Remarks,
        Type = (TransactionType)proto.Type,
        PayerWalletId = proto.PayerWalletId is not null ? Guid.Parse(proto.PayerWalletId) : null,
        PayeeWalletId = proto.PayeeWalletId is not null ? Guid.Parse(proto.PayeeWalletId) : null,
    };
}

public class SnWalletTransferRequest : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public WalletTransferRequestStatus Status { get; set; } = WalletTransferRequestStatus.Active;
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal Amount { get; set; }
    [MaxLength(4096)] public string? Remark { get; set; }
    public bool Freeze { get; set; }
    public bool RequireConfirmation { get; set; }
    public Instant ExpiresAt { get; set; }
    public Instant? FulfilledAt { get; set; }

    public Guid CreatorAccountId { get; set; }
    public Guid PayeeWalletId { get; set; }
    public SnWallet PayeeWallet { get; set; } = null!;
    public Guid? TransactionId { get; set; }
    public SnWalletTransaction? Transaction { get; set; }

    [NotMapped] public bool IsExpired => Status == WalletTransferRequestStatus.Expired || ExpiresAt < SystemClock.Instance.GetCurrentInstant();
}
