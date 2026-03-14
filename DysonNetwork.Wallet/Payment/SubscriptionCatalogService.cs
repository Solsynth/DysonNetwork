using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Wallet.Payment;

public class SubscriptionCatalogService(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<SubscriptionCatalogService> logger
)
{
    private readonly AppDatabase _db = db;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<SubscriptionCatalogService> _logger = logger;

    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var options = _configuration.GetSection("Payment:SubscriptionCatalog").Get<SubscriptionCatalogSeedOptions>();
        if (options is null) return;

        var existingSettings = await _db.WalletSubscriptionCatalogSettings.FirstOrDefaultAsync(cancellationToken);
        if (existingSettings is null)
        {
            _db.WalletSubscriptionCatalogSettings.Add(new SnWalletSubscriptionCatalogSettings
            {
                GiftPolicyDefaults = options.Settings.GiftPolicyDefaults.Clone()
            });
        }

        var identifiers = options.Definitions.Select(x => x.Identifier).ToList();
        var existingDefinitions = await _db.WalletSubscriptionDefinitions
            .Where(x => identifiers.Contains(x.Identifier))
            .Select(x => x.Identifier)
            .ToListAsync(cancellationToken);

        foreach (var definition in options.Definitions.Where(x => !existingDefinitions.Contains(x.Identifier)))
        {
            _db.WalletSubscriptionDefinitions.Add(new SnWalletSubscriptionDefinition
            {
                Identifier = definition.Identifier,
                GroupIdentifier = definition.GroupIdentifier,
                DisplayName = definition.DisplayName,
                Currency = definition.Currency,
                BasePrice = definition.BasePrice,
                PerkLevel = definition.PerkLevel,
                MinimumAccountLevel = definition.MinimumAccountLevel,
                ExperienceMultiplier = definition.ExperienceMultiplier,
                GoldenPointReward = definition.GoldenPointReward,
                PaymentPolicy = new SubscriptionPaymentPolicy
                {
                    AllowInternalWallet = definition.PaymentPolicy.AllowInternalWallet,
                    AllowExternal = definition.PaymentPolicy.AllowExternal,
                    AllowInternalWalletRenewal = definition.PaymentPolicy.AllowInternalWalletRenewal,
                    AllowedMethods = definition.PaymentPolicy.AllowedMethods.ToList()
                },
                GiftPolicy = definition.GiftPolicy is null ? null : new SubscriptionGiftPolicy
                {
                    AllowPurchase = definition.GiftPolicy.AllowPurchase,
                    MinimumAccountLevel = definition.GiftPolicy.MinimumAccountLevel,
                    AllowPerkSubscriptionBypass = definition.GiftPolicy.AllowPerkSubscriptionBypass,
                    RollingPurchaseLimit = definition.GiftPolicy.RollingPurchaseLimit,
                    RollingWindowDays = definition.GiftPolicy.RollingWindowDays,
                    GiftDurationDays = definition.GiftPolicy.GiftDurationDays,
                    SubscriptionDurationDays = definition.GiftPolicy.SubscriptionDurationDays
                },
                ProviderMappings = definition.ProviderMappings.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value.ToList(),
                    StringComparer.OrdinalIgnoreCase
                )
            });
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded subscription catalog definitions from configuration.");
        }
    }

    public Task<SnWalletSubscriptionDefinition?> GetDefinitionAsync(string identifier, CancellationToken cancellationToken = default)
    {
        return _db.WalletSubscriptionDefinitions.FirstOrDefaultAsync(x => x.Identifier == identifier, cancellationToken);
    }

    public async Task<SnWalletSubscriptionDefinition?> ResolveDefinitionAsync(
        string provider,
        string externalId,
        CancellationToken cancellationToken = default
    )
    {
        var definitions = await _db.WalletSubscriptionDefinitions.ToListAsync(cancellationToken);
        return definitions.FirstOrDefault(def =>
            def.ProviderMappings.TryGetValue(provider, out var mapped) &&
            mapped.Any(id => string.Equals(id, externalId, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<List<string>> GetGroupIdentifiersAsync(string? groupIdentifier, string fallbackIdentifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupIdentifier))
            return [fallbackIdentifier];

        return await _db.WalletSubscriptionDefinitions
            .Where(x => x.GroupIdentifier == groupIdentifier)
            .Select(x => x.Identifier)
            .ToListAsync(cancellationToken);
    }

    public async Task<SubscriptionGiftPolicy> GetGiftPolicyAsync(
        SnWalletSubscriptionDefinition definition,
        CancellationToken cancellationToken = default
    )
    {
        var settings = await _db.WalletSubscriptionCatalogSettings.FirstOrDefaultAsync(cancellationToken);
        var defaults = settings?.GiftPolicyDefaults.Clone() ?? new SubscriptionGiftPolicy();
        return defaults.Merge(definition.GiftPolicy);
    }
}
