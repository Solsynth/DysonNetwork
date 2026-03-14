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
        else if (!GiftPoliciesEqual(existingSettings.GiftPolicyDefaults, options.Settings.GiftPolicyDefaults))
        {
            existingSettings.GiftPolicyDefaults = options.Settings.GiftPolicyDefaults.Clone();
            _db.WalletSubscriptionCatalogSettings.Update(existingSettings);
        }

        var identifiers = options.Definitions.Select(x => x.Identifier).ToList();
        var existingDefinitions = await _db.WalletSubscriptionDefinitions
            .Where(x => identifiers.Contains(x.Identifier))
            .ToListAsync(cancellationToken);
        var existingDefinitionMap = existingDefinitions.ToDictionary(x => x.Identifier, StringComparer.Ordinal);

        foreach (var definition in options.Definitions)
        {
            if (!existingDefinitionMap.TryGetValue(definition.Identifier, out var existingDefinition))
            {
                _db.WalletSubscriptionDefinitions.Add(BuildDefinition(definition));
                continue;
            }

            var changed = ApplyDefinitionUpdates(existingDefinition, definition);
            if (changed)
                _db.WalletSubscriptionDefinitions.Update(existingDefinition);
        }

        if (_db.ChangeTracker.HasChanges())
        {
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Synchronized subscription catalog definitions from configuration.");
        }
    }

    private static SnWalletSubscriptionDefinition BuildDefinition(SubscriptionCatalogSeedDefinition definition)
    {
        return new SnWalletSubscriptionDefinition
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
            PaymentPolicy = ClonePaymentPolicy(definition.PaymentPolicy),
            GiftPolicy = definition.GiftPolicy is null ? null : definition.GiftPolicy.Clone(),
            ProviderMappings = CloneProviderMappings(definition.ProviderMappings)
        };
    }

    private static bool ApplyDefinitionUpdates(
        SnWalletSubscriptionDefinition existingDefinition,
        SubscriptionCatalogSeedDefinition definition
    )
    {
        var changed = false;

        changed |= SetIfDifferent(existingDefinition.GroupIdentifier, definition.GroupIdentifier, value => existingDefinition.GroupIdentifier = value);
        changed |= SetIfDifferent(existingDefinition.DisplayName, definition.DisplayName, value => existingDefinition.DisplayName = value);
        changed |= SetIfDifferent(existingDefinition.Currency, definition.Currency, value => existingDefinition.Currency = value);
        changed |= SetIfDifferent(existingDefinition.BasePrice, definition.BasePrice, value => existingDefinition.BasePrice = value);
        changed |= SetIfDifferent(existingDefinition.PerkLevel, definition.PerkLevel, value => existingDefinition.PerkLevel = value);
        changed |= SetIfDifferent(existingDefinition.MinimumAccountLevel, definition.MinimumAccountLevel, value => existingDefinition.MinimumAccountLevel = value);
        changed |= SetIfDifferent(existingDefinition.ExperienceMultiplier, definition.ExperienceMultiplier, value => existingDefinition.ExperienceMultiplier = value);
        changed |= SetIfDifferent(existingDefinition.GoldenPointReward, definition.GoldenPointReward, value => existingDefinition.GoldenPointReward = value);

        if (!PaymentPoliciesEqual(existingDefinition.PaymentPolicy, definition.PaymentPolicy))
        {
            existingDefinition.PaymentPolicy = ClonePaymentPolicy(definition.PaymentPolicy);
            changed = true;
        }

        if (!GiftPoliciesEqual(existingDefinition.GiftPolicy, definition.GiftPolicy))
        {
            existingDefinition.GiftPolicy = definition.GiftPolicy is null ? null : definition.GiftPolicy.Clone();
            changed = true;
        }

        if (!ProviderMappingsEqual(existingDefinition.ProviderMappings, definition.ProviderMappings))
        {
            existingDefinition.ProviderMappings = CloneProviderMappings(definition.ProviderMappings);
            changed = true;
        }

        return changed;
    }

    private static SubscriptionPaymentPolicy ClonePaymentPolicy(SubscriptionPaymentPolicy policy)
    {
        return new SubscriptionPaymentPolicy
        {
            AllowInternalWallet = policy.AllowInternalWallet,
            AllowExternal = policy.AllowExternal,
            AllowInternalWalletRenewal = policy.AllowInternalWalletRenewal,
            AllowedMethods = policy.AllowedMethods.ToList()
        };
    }

    private static Dictionary<string, List<string>> CloneProviderMappings(Dictionary<string, List<string>> mappings)
    {
        return mappings.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.ToList(),
            StringComparer.OrdinalIgnoreCase
        );
    }

    private static bool PaymentPoliciesEqual(SubscriptionPaymentPolicy? left, SubscriptionPaymentPolicy? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        return left.AllowInternalWallet == right.AllowInternalWallet &&
               left.AllowExternal == right.AllowExternal &&
               left.AllowInternalWalletRenewal == right.AllowInternalWalletRenewal &&
               left.AllowedMethods.SequenceEqual(right.AllowedMethods, StringComparer.OrdinalIgnoreCase);
    }

    private static bool GiftPoliciesEqual(SubscriptionGiftPolicy? left, SubscriptionGiftPolicy? right)
    {
        if (left is null && right is null) return true;
        if (left is null || right is null) return false;

        return left.AllowPurchase == right.AllowPurchase &&
               left.MinimumAccountLevel == right.MinimumAccountLevel &&
               left.AllowPerkSubscriptionBypass == right.AllowPerkSubscriptionBypass &&
               left.RollingPurchaseLimit == right.RollingPurchaseLimit &&
               left.RollingWindowDays == right.RollingWindowDays &&
               left.GiftDurationDays == right.GiftDurationDays &&
               left.SubscriptionDurationDays == right.SubscriptionDurationDays;
    }

    private static bool ProviderMappingsEqual(
        Dictionary<string, List<string>> left,
        Dictionary<string, List<string>> right
    )
    {
        if (left.Count != right.Count) return false;

        foreach (var kv in left)
        {
            if (!right.TryGetValue(kv.Key, out var values)) return false;
            if (!kv.Value.SequenceEqual(values, StringComparer.OrdinalIgnoreCase)) return false;
        }

        return true;
    }

    private static bool SetIfDifferent<T>(T currentValue, T newValue, Action<T> setter)
    {
        if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
            return false;

        setter(newValue);
        return true;
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

    public async Task<string?> GetProviderReferenceAsync(
        string identifier,
        string provider,
        string? preferredReference = null,
        CancellationToken cancellationToken = default
    )
    {
        var definition = await GetDefinitionAsync(identifier, cancellationToken);
        if (definition is null) return null;
        if (!definition.ProviderMappings.TryGetValue(provider, out var mapped) || mapped.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(preferredReference))
            return mapped.FirstOrDefault(x => string.Equals(x, preferredReference, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(provider, SubscriptionPaymentMethod.Paddle, StringComparison.OrdinalIgnoreCase))
        {
            var preferredPrice = mapped.FirstOrDefault(x => x.StartsWith("pri_", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(preferredPrice))
                return preferredPrice;
        }

        return mapped[0];
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
