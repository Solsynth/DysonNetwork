using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Wallet;

public class Wallet : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public ICollection<WalletPocket> Pockets { get; set; } = new List<WalletPocket>();
    
    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    public Shared.Proto.Wallet ToProtoValue()
    {
        var proto = new Shared.Proto.Wallet
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

    public static Wallet FromProtoValue(Shared.Proto.Wallet proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        AccountId = Guid.Parse(proto.AccountId),
        Pockets = proto.Pockets.Select(WalletPocket.FromProtoValue).ToList(),
    };
}

public class WalletPocket : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal Amount { get; set; }
    
    public Guid WalletId { get; set; }
    [JsonIgnore] public Wallet Wallet { get; set; } = null!;

    public Shared.Proto.WalletPocket ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Currency = Currency,
        Amount = Amount.ToString(CultureInfo.CurrentCulture),
        WalletId = WalletId.ToString(),
    };

    public static WalletPocket FromProtoValue(Shared.Proto.WalletPocket proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Currency = proto.Currency,
        Amount = decimal.Parse(proto.Amount),
        WalletId = Guid.Parse(proto.WalletId),
    };
}