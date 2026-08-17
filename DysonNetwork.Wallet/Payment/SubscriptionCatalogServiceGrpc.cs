using System.Text.Json;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Wallet.Models;
using Grpc.Core;

namespace DysonNetwork.Wallet.Payment;

/// <summary>
/// Internal gRPC surface for services to register their own app product catalog
/// definitions (workspace plans, quota addons, ...) in the wallet. Each caller claims
/// an app identifier and may only create/update definitions bearing that identifier —
/// platform subscriptions stay human-admin-managed via the REST admin endpoints.
/// </summary>
public class SubscriptionCatalogServiceGrpc(
    SubscriptionCatalogService catalog
) : DySubscriptionCatalogService.DySubscriptionCatalogServiceBase
{
    public override async Task<DyUpsertSubscriptionDefinitionResponse> UpsertDefinition(
        DyUpsertSubscriptionDefinitionRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.AppIdentifier))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "app_identifier is required."));

        if (string.IsNullOrWhiteSpace(request.DefinitionJson))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "definition_json is required."));

        SubscriptionCatalogSeedDefinition definition;
        try
        {
            definition = JsonSerializer.Deserialize<SubscriptionCatalogSeedDefinition>(request.DefinitionJson)
                ?? throw new JsonException("definition_json did not deserialize to a definition.");
        }
        catch (JsonException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"definition_json is invalid: {ex.Message}"));
        }

        if (string.IsNullOrWhiteSpace(definition.Identifier) ||
            string.IsNullOrWhiteSpace(definition.DisplayName) ||
            string.IsNullOrWhiteSpace(definition.Currency))
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "definition_json must include identifier, display_name, and currency."));
        }

        // Ownership: a service may only manage definitions bearing its own app identifier.
        // The stored AppIdentifier is always the claimed one; anything inside the JSON is ignored.
        definition.AppIdentifier = request.AppIdentifier;
        var existing = await catalog.GetDefinitionAsync(definition.Identifier, context.CancellationToken);
        if (existing is not null &&
            !string.Equals(existing.AppIdentifier, request.AppIdentifier, StringComparison.Ordinal))
        {
            throw new RpcException(new Status(
                StatusCode.FailedPrecondition,
                $"Definition {definition.Identifier} is managed by app {existing.AppIdentifier ?? "platform"}."));
        }

        var (_, created) = await catalog.UpsertDefinitionFromSeedAsync(definition, context.CancellationToken);

        return new DyUpsertSubscriptionDefinitionResponse
        {
            Created = created,
            Identifier = definition.Identifier
        };
    }

    public override async Task<DyListSubscriptionDefinitionsResponse> ListDefinitions(
        DyListSubscriptionDefinitionsRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.AppIdentifier))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "app_identifier is required."));

        var definitions = await catalog.GetDefinitionsByAppAsync(request.AppIdentifier, context.CancellationToken);
        var response = new DyListSubscriptionDefinitionsResponse();
        foreach (var definition in definitions)
            response.DefinitionsJson.Add(JsonSerializer.Serialize(ToSeedDefinition(definition)));

        return response;
    }

    private static SubscriptionCatalogSeedDefinition ToSeedDefinition(SnWalletSubscriptionDefinition definition)
    {
        return new SubscriptionCatalogSeedDefinition
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
            DisplayConfig = definition.DisplayConfig,
            PaymentPolicy = definition.PaymentPolicy,
            GiftPolicy = definition.GiftPolicy,
            ProviderMappings = definition.ProviderMappings,
            AppIdentifier = definition.AppIdentifier
        };
    }
}
