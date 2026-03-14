using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Identifier), IsUnique = true)]
public class SnWalletSubscriptionDefinition : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(4096)] public string Identifier { get; set; } = null!;
    [MaxLength(4096)] public string? GroupIdentifier { get; set; }
    [MaxLength(4096)] public string DisplayName { get; set; } = null!;
    [MaxLength(128)] public string Currency { get; set; } = null!;
    public decimal BasePrice { get; set; }
    public int PerkLevel { get; set; }
    public int? MinimumAccountLevel { get; set; }
    public decimal? ExperienceMultiplier { get; set; }
    public int? GoldenPointReward { get; set; }

    [Column(TypeName = "jsonb")] public SubscriptionPaymentPolicy PaymentPolicy { get; set; } = new();
    [Column(TypeName = "jsonb")] public SubscriptionGiftPolicy? GiftPolicy { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, List<string>> ProviderMappings { get; set; } = new();

    public bool IsPaymentMethodAllowed(string paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod)) return false;

        var normalized = paymentMethod.Trim().ToLowerInvariant();
        if (PaymentPolicy.AllowedMethods.Count > 0 &&
            !PaymentPolicy.AllowedMethods.Any(m => string.Equals(m, normalized, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (normalized == SubscriptionPaymentMethod.InAppWallet)
            return PaymentPolicy.AllowInternalWallet;
        if (normalized == SubscriptionPaymentMethod.Gift)
            return PaymentPolicy.AllowedMethods.Count == 0 ||
                   PaymentPolicy.AllowedMethods.Any(m => string.Equals(m, normalized, StringComparison.OrdinalIgnoreCase));

        return PaymentPolicy.AllowExternal;
    }
}

public class SnWalletSubscriptionCatalogSettings : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column(TypeName = "jsonb")] public SubscriptionGiftPolicy GiftPolicyDefaults { get; set; } = new();
}

public class SubscriptionPaymentPolicy
{
    public bool AllowInternalWallet { get; set; } = true;
    public bool AllowExternal { get; set; } = true;
    public bool AllowInternalWalletRenewal { get; set; } = false;
    public List<string> AllowedMethods { get; set; } = [];
}

public class SubscriptionGiftPolicy
{
    public bool AllowPurchase { get; set; } = true;
    public int? MinimumAccountLevel { get; set; }
    public bool AllowPerkSubscriptionBypass { get; set; } = true;
    public int? RollingPurchaseLimit { get; set; }
    public int? RollingWindowDays { get; set; }
    public int? GiftDurationDays { get; set; }
    public int? SubscriptionDurationDays { get; set; }

    public SubscriptionGiftPolicy Merge(SubscriptionGiftPolicy? overrides)
    {
        if (overrides is null) return this.Clone();

        return new SubscriptionGiftPolicy
        {
            AllowPurchase = overrides.AllowPurchase,
            MinimumAccountLevel = overrides.MinimumAccountLevel ?? MinimumAccountLevel,
            AllowPerkSubscriptionBypass = overrides.AllowPerkSubscriptionBypass,
            RollingPurchaseLimit = overrides.RollingPurchaseLimit ?? RollingPurchaseLimit,
            RollingWindowDays = overrides.RollingWindowDays ?? RollingWindowDays,
            GiftDurationDays = overrides.GiftDurationDays ?? GiftDurationDays,
            SubscriptionDurationDays = overrides.SubscriptionDurationDays ?? SubscriptionDurationDays
        };
    }
}

public static class SubscriptionCatalogPolicyExtensions
{
    public static SubscriptionGiftPolicy Clone(this SubscriptionGiftPolicy policy) => new()
    {
        AllowPurchase = policy.AllowPurchase,
        MinimumAccountLevel = policy.MinimumAccountLevel,
        AllowPerkSubscriptionBypass = policy.AllowPerkSubscriptionBypass,
        RollingPurchaseLimit = policy.RollingPurchaseLimit,
        RollingWindowDays = policy.RollingWindowDays,
        GiftDurationDays = policy.GiftDurationDays,
        SubscriptionDurationDays = policy.SubscriptionDurationDays
    };
}

public class SubscriptionCatalogSeedOptions
{
    public SubscriptionCatalogSeedSettings Settings { get; set; } = new();
    public List<SubscriptionCatalogSeedDefinition> Definitions { get; set; } = [];
}

public class SubscriptionCatalogSeedSettings
{
    public SubscriptionGiftPolicy GiftPolicyDefaults { get; set; } = new();
}

public class SubscriptionCatalogSeedDefinition
{
    public string Identifier { get; set; } = null!;
    public string? GroupIdentifier { get; set; }
    public string DisplayName { get; set; } = null!;
    public string Currency { get; set; } = null!;
    public decimal BasePrice { get; set; }
    public int PerkLevel { get; set; }
    public int? MinimumAccountLevel { get; set; }
    public decimal? ExperienceMultiplier { get; set; }
    public int? GoldenPointReward { get; set; }
    public SubscriptionPaymentPolicy PaymentPolicy { get; set; } = new();
    public SubscriptionGiftPolicy? GiftPolicy { get; set; }
    public Dictionary<string, List<string>> ProviderMappings { get; set; } = new();
}
