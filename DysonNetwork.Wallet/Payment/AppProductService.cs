using System.Text.Json;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Wallet.Models;
using DysonNetwork.Wallet.Payment.PaymentHandlers;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

/// <summary>
/// App-owned product purchases (workspace plans and quota addons) bought directly via
/// Apple IAP or Paddle. Reuses the wallet's existing webhook endpoints: the provider
/// transaction resolves to a catalog definition with a non-empty <c>AppIdentifier</c>,
/// which routes it here instead of the platform-subscription path.
/// </summary>
public class AppProductService(
    AppDatabase db,
    PaymentService payment,
    WalletService wallets,
    SubscriptionCatalogService catalog,
    IEventBus eventBus,
    DyAccountService.DyAccountServiceClient accountGrpc,
    ILogger<AppProductService> logger
)
{
    /// <summary>True when the provider order maps to an app-owned catalog product.</summary>
    public async Task<bool> IsAppProductOrderAsync(ISubscriptionOrder order, CancellationToken ct = default)
    {
        var definition = await catalog.ResolveDefinitionAsync(order.Provider, order.SubscriptionId, ct);
        return definition is not null && !string.IsNullOrWhiteSpace(definition.AppIdentifier);
    }

    /// <summary>
    /// Applies a paid app-product order: resolves the owning account and workspace, creates
    /// a Paid wallet order (idempotent per provider order), and publishes the existing
    /// <c>PaymentOrderEvent</c> for downstream consumers (Valve grants/activations).
    /// </summary>
    public async Task<SnWalletOrder> ApplyAppProductOrderAsync(
        ISubscriptionOrder order,
        Dictionary<string, object>? extraMeta = null,
        CancellationToken ct = default
    )
    {
        var definition = await catalog.ResolveDefinitionAsync(order.Provider, order.SubscriptionId, ct);
        if (definition is null || string.IsNullOrWhiteSpace(definition.AppIdentifier))
            throw new InvalidOperationException(
                $"Subscription {order.SubscriptionId} is not an app product on {order.Provider}."
            );

        if (!definition.IsPaymentMethodAllowed(order.Provider))
            throw new InvalidOperationException(
                $"Payment method {order.Provider} is not allowed for {definition.DisplayName}."
            );

        var accountId = await ResolveAccountIdForOrderAsync(order, ct);
        var workspaceId = await ResolveWorkspaceIdAsync(order, definition, extraMeta, ct);
        if (workspaceId is null)
            throw new InvalidOperationException("App product order is missing a workspace_id; cannot apply.");

        var wallet = await wallets.GetAccountWalletAsync(accountId)
            ?? await wallets.CreateWalletAsync(accountId: accountId);

        var quantity = order is AppleAppStoreTransaction appleOrder ? appleOrder.Quantity : 1;
        var amount = definition.BasePrice * quantity;
        var remarks = $"app-product:{definition.Identifier}:{order.Provider}:{order.Id}";

        // Idempotency: this provider order was already applied.
        var existing = await db.PaymentOrders
            .FirstOrDefaultAsync(
                o => o.AppIdentifier == definition.AppIdentifier &&
                     o.ProductIdentifier == definition.Identifier &&
                     o.Remarks == remarks,
                ct);
        if (existing is not null)
            return existing;

        var durationDays = order.Duration > Duration.Zero ? (int)order.Duration.TotalDays : (int?)null;
        var meta = new Dictionary<string, object>
        {
            ["app_product"] = definition.Identifier,
            ["provider"] = order.Provider,
            ["provider_order_id"] = order.Id,
            ["provider_reference_id"] = order.SubscriptionId,
            ["account_id"] = accountId.ToString(),
            ["workspace_id"] = workspaceId.ToString(),
            ["quantity"] = quantity,
            ["duration_days"] = durationDays
        };

        if (order is AppleAppStoreTransaction apple && !string.IsNullOrWhiteSpace(apple.Payload.OriginalTransactionId))
            meta["original_transaction_id"] = apple.Payload.OriginalTransactionId;

        var created = await payment.CreateOrderAsync(
            wallet.Id,
            definition.Currency,
            amount,
            appIdentifier: definition.AppIdentifier,
            productIdentifier: definition.Identifier,
            remarks: remarks,
            meta: meta,
            reuseable: false
        );

        created.Status = OrderStatus.Paid;
        await db.SaveChangesAsync(ct);

        await eventBus.PublishAsync(PaymentOrderEventBase.Type, new PaymentOrderEvent
        {
            OrderId = created.Id,
            WalletId = wallet.Id,
            AccountId = accountId,
            AppIdentifier = created.AppIdentifier,
            ProductIdentifier = created.ProductIdentifier,
            Meta = created.Meta ?? [],
            Status = (int)created.Status,
        });

        logger.LogInformation(
            "Applied app product {Identifier} for account {AccountId} workspace {WorkspaceId} (order {OrderId})",
            definition.Identifier,
            accountId,
            workspaceId,
            created.Id
        );

        return created;
    }

    private async Task<Guid> ResolveAccountIdForOrderAsync(ISubscriptionOrder order, CancellationToken ct)
    {
        if (order is AfdianWebhookAfdianOrderDetails afdianWebhookOrder &&
            Guid.TryParse(afdianWebhookOrder.CustomOrderId, out var customOrderAccountId))
            return customOrderAccountId;

        if (!string.IsNullOrWhiteSpace(order.Provider) && !string.IsNullOrWhiteSpace(order.AccountId))
        {
            try
            {
                var accountProto = await accountGrpc.GetAccountByConnectionAsync(
                    new DyGetAccountByConnectionRequest
                    {
                        Provider = order.Provider,
                        ProvidedIdentifier = order.AccountId
                    },
                    cancellationToken: ct
                );
                if (Guid.TryParse(accountProto.Id, out var accountId))
                    return accountId;
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.InvalidArgument)
            {
                logger.LogDebug(
                    ex,
                    "No linked account found for provider {Provider} identifier {AccountIdentifier}. Falling back to guid parsing.",
                    order.Provider,
                    order.AccountId
                );
            }
        }

        if (Guid.TryParse(order.AccountId, out var parsedAccountId))
            return parsedAccountId;

        throw new InvalidOperationException("Unable to resolve the account for this app product purchase.");
    }

    /// <summary>
    /// Resolves the target workspace: explicit request meta first, then Paddle custom_data,
    /// then (Apple) inheritance from a prior order with the same original transaction id
    /// (renewals carry no account/workspace data of their own).
    /// </summary>
    private async Task<Guid?> ResolveWorkspaceIdAsync(
        ISubscriptionOrder order,
        SnWalletSubscriptionDefinition definition,
        Dictionary<string, object>? extraMeta,
        CancellationToken ct
    )
    {
        if (extraMeta is not null &&
            extraMeta.TryGetValue("workspace_id", out var wsValue) &&
            wsValue is not null &&
            Guid.TryParse(wsValue.ToString(), out var extraWorkspaceId))
            return extraWorkspaceId;

        if (order is PaddleTransaction paddle && paddle.CustomData is not null &&
            paddle.CustomData.TryGetValue("workspace_id", out var paddleWs) &&
            Guid.TryParse(paddleWs.ToString(), out var paddleWorkspaceId))
            return paddleWorkspaceId;

        if (order is AppleAppStoreTransaction apple && !string.IsNullOrWhiteSpace(apple.Payload.OriginalTransactionId))
        {
            var candidates = await db.PaymentOrders
                .AsNoTracking()
                .Where(o => o.AppIdentifier == definition.AppIdentifier &&
                            o.ProductIdentifier == definition.Identifier &&
                            o.Meta != null)
                .ToListAsync(ct);

            var prior = candidates.FirstOrDefault(o =>
                o.Meta!.TryGetValue("original_transaction_id", out var v) &&
                string.Equals(v?.ToString(), apple.Payload.OriginalTransactionId, StringComparison.OrdinalIgnoreCase));

            if (prior?.Meta is not null &&
                prior.Meta.TryGetValue("workspace_id", out var priorWs) &&
                priorWs is not null &&
                Guid.TryParse(priorWs.ToString(), out var inheritedWorkspaceId))
                return inheritedWorkspaceId;
        }

        return null;
    }
}
