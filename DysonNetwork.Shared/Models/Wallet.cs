using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using MessagePack;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public class SnWallet : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public List<SnWalletPocket> Pockets { get; set; } = new List<SnWalletPocket>();

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    public DyWallet ToProtoValue()
    {
        var proto = new DyWallet
        {
            Id = Id.ToString(),
            AccountId = AccountId.ToString(),
        };

        foreach (var pocket in Pockets)
        {
            proto.Pockets.Add(pocket.ToProtoValue());
        }

        return proto;
    }

    public static SnWallet FromProtoValue(DyWallet proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        AccountId = Guid.Parse(proto.AccountId),
        Pockets = [.. proto.Pockets.Select(SnWalletPocket.FromProtoValue)],
    };
}

public class SnWalletPocket : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal Amount { get; set; }

    public Guid WalletId { get; set; }
    [IgnoreMember] [JsonIgnore] public SnWallet Wallet { get; set; } = null!;

    public DyWalletPocket ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Currency = Currency,
        Amount = Amount.ToString(CultureInfo.CurrentCulture),
        WalletId = WalletId.ToString(),
    };

    public static SnWalletPocket FromProtoValue(DyWalletPocket proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Currency = proto.Currency,
        Amount = decimal.Parse(proto.Amount),
        WalletId = Guid.Parse(proto.WalletId),
    };
}

public enum FundSplitType
{
    Even,
    Random
}

public enum FundStatus
{
    Created,
    PartiallyReceived,
    FullyReceived,
    Expired,
    Refunded
}

public class SnWalletFund : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public int AmountOfSplits { get; set; }
    public FundSplitType SplitType { get; set; }
    public FundStatus Status { get; set; } = FundStatus.Created;
    [MaxLength(4096)] public string? Message { get; set; }
    public bool IsOpen { get; set; }

    // Creator
    public Guid CreatorAccountId { get; set; }
    [NotMapped] public SnAccount CreatorAccount { get; set; } = null!;

    // Recipients
    public List<SnWalletFundRecipient> Recipients { get; set; } = new List<SnWalletFundRecipient>();

    // Expiration
    public Instant ExpiredAt { get; set; }

    public DyWalletFund ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Currency = Currency,
        TotalAmount = TotalAmount.ToString(CultureInfo.InvariantCulture),
        RemainingAmount = RemainingAmount.ToString(CultureInfo.InvariantCulture),
        SplitType = (DyFundSplitType)SplitType,
        Status = (DyFundStatus)Status,
        Message = Message,
        CreatorAccountId = CreatorAccountId.ToString(),
        ExpiredAt = ExpiredAt.ToTimestamp(),
    };

    public DyWalletFund ToProtoValueWithRecipients()
    {
        var proto = new DyWalletFund
        {
            Id = Id.ToString(),
            Currency = Currency,
            TotalAmount = TotalAmount.ToString(CultureInfo.InvariantCulture),
            RemainingAmount = RemainingAmount.ToString(CultureInfo.InvariantCulture),
            SplitType = (DyFundSplitType)SplitType,
            Status = (DyFundStatus)Status,
            Message = Message,
            CreatorAccountId = CreatorAccountId.ToString(),
            ExpiredAt = ExpiredAt.ToTimestamp(),
        };

        foreach (var recipient in Recipients)
        {
            proto.Recipients.Add(recipient.ToProtoValue());
        }

        return proto;
    }

    public static SnWalletFund FromProtoValue(DyWalletFund proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Currency = proto.Currency,
        TotalAmount = decimal.Parse(proto.TotalAmount),
        RemainingAmount = proto.RemainingAmount is not null
            ? decimal.Parse(proto.RemainingAmount)
            : decimal.Parse(proto.TotalAmount),
        SplitType = (FundSplitType)proto.SplitType,
        Status = (FundStatus)proto.Status,
        Message = proto.Message,
        CreatorAccountId = Guid.Parse(proto.CreatorAccountId),
        ExpiredAt = proto.ExpiredAt.ToInstant(),
    };
}

public class SnWalletFundRecipient : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FundId { get; set; }
    [JsonIgnore] public SnWalletFund Fund { get; set; } = null!;

    public Guid RecipientAccountId { get; set; }
    [NotMapped] public SnAccount RecipientAccount { get; set; } = null!;

    public decimal Amount { get; set; }
    public bool IsReceived { get; set; } = false;
    public Instant? ReceivedAt { get; set; }

    public DyWalletFundRecipient ToProtoValue() => new()
    {
        Id = Id.ToString(),
        FundId = FundId.ToString(),
        RecipientAccountId = RecipientAccountId.ToString(),
        Amount = Amount.ToString(CultureInfo.InvariantCulture),
        IsReceived = IsReceived,
        ReceivedAt = ReceivedAt?.ToTimestamp(),
    };

    public static SnWalletFundRecipient FromProtoValue(DyWalletFundRecipient proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        FundId = Guid.Parse(proto.FundId),
        RecipientAccountId = Guid.Parse(proto.RecipientAccountId),
        Amount = decimal.Parse(proto.Amount),
        IsReceived = proto.IsReceived,
        ReceivedAt = proto.ReceivedAt?.ToInstant(),
    };
}
