using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Data;
using NodaTime;
using NodaTime.Serialization.Protobuf;

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

    public Shared.Proto.Order ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Status = (Shared.Proto.OrderStatus)Status,
        Currency = Currency,
        Remarks = Remarks,
        AppIdentifier = AppIdentifier,
        Meta = Meta == null
            ? null
            : Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Meta)),
        Amount = Amount.ToString(),
        ExpiredAt = ExpiredAt.ToTimestamp(),
        PayeeWalletId = PayeeWalletId?.ToString(),
        TransactionId = TransactionId?.ToString(),
        Transaction = Transaction?.ToProtoValue(),
    };

    public static Order FromProtoValue(Shared.Proto.Order proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Status = (OrderStatus)proto.Status,
        Currency = proto.Currency,
        Remarks = proto.Remarks,
        AppIdentifier = proto.AppIdentifier,
        Meta = proto.HasMeta
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(proto.Meta.ToByteArray())
            : null,
        Amount = decimal.Parse(proto.Amount),
        ExpiredAt = proto.ExpiredAt.ToInstant(),
        PayeeWalletId = proto.HasPayeeWalletId ? Guid.Parse(proto.PayeeWalletId) : null,
        TransactionId = proto.HasTransactionId ? Guid.Parse(proto.TransactionId) : null,
        Transaction = proto.Transaction is not null ? Transaction.FromProtoValue(proto.Transaction) : null,
    };
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

    public Shared.Proto.Transaction ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Currency = Currency,
        Amount = Amount.ToString(),
        Remarks = Remarks,
        Type = (Shared.Proto.TransactionType)Type,
        PayerWalletId = PayerWalletId?.ToString(),
        PayeeWalletId = PayeeWalletId?.ToString(),
    };

    public static Transaction FromProtoValue(Shared.Proto.Transaction proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Currency = proto.Currency,
        Amount = decimal.Parse(proto.Amount),
        Remarks = proto.Remarks,
        Type = (TransactionType)proto.Type,
        PayerWalletId = proto.HasPayerWalletId ? Guid.Parse(proto.PayerWalletId) : null,
        PayeeWalletId = proto.HasPayeeWalletId ? Guid.Parse(proto.PayeeWalletId) : null,
    };
}