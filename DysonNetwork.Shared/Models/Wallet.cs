using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.Models;

public class SnWallet : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public ICollection<SnWalletPocket> Pockets { get; set; } = new List<SnWalletPocket>();
    
    public Guid AccountId { get; set; }
    public SnAccount Account { get; set; } = null!;

    public Proto.Wallet ToProtoValue()
    {
        var proto = new Proto.Wallet
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

    public static SnWallet FromProtoValue(Proto.Wallet proto) => new()
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
    [JsonIgnore] public SnWallet Wallet { get; set; } = null!;

    public Proto.WalletPocket ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Currency = Currency,
        Amount = Amount.ToString(CultureInfo.CurrentCulture),
        WalletId = WalletId.ToString(),
    };

    public static SnWalletPocket FromProtoValue(Proto.WalletPocket proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Currency = proto.Currency,
        Amount = decimal.Parse(proto.Amount),
        WalletId = Guid.Parse(proto.WalletId),
    };
}