using DysonNetwork.Shared.Models;

namespace DysonNetwork.Wallet.Models;

public class ProductOptions
{
    public WalletProductDefinitionOptions GoldCurrency { get; set; } = new();
}

public class WalletProductDefinitionOptions
{
    public string Identifier { get; set; } = "wallet.golds_resupply_pack";
    public string DisplayName { get; set; } = "Golds Resupply Pack";
    public string Currency { get; set; } = WalletCurrency.GoldenPoint;
    public Dictionary<string, Dictionary<string, decimal>> ProviderMappings { get; set; } = [];
}
