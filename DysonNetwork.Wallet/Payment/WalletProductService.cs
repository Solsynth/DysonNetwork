using System.Globalization;
using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Wallet.Models;
using DysonNetwork.Wallet.Payment.PaymentHandlers;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Wallet.Payment;

public class WalletProductService(
    AppDatabase db,
    PaymentService payment,
    WalletService wallets,
    DyAccountService.DyAccountServiceClient accountGrpc,
    RemoteAccountService remoteAccounts,
    DyRingService.DyRingServiceClient pusher,
    ILocalizationService localizer,
    IConfiguration configuration,
    ILogger<WalletProductService> logger
)
{
    public const string GoldsResupplyPackKey = "golds_resupply_pack";

    public WalletProductDefinitionOptions GetGoldsResupplyPackDefinition()
    {
        var options = configuration.GetSection("Payment:Product").Get<ProductOptions>();
        return options?.GoldCurrency ?? new WalletProductDefinitionOptions();
    }

    public (WalletProductDefinitionOptions Product, string ProviderReference, decimal Amount) PreparePaddleGoldsResupplyPack(
        string? providerReference = null
    )
    {
        return ResolveProductReference(SubscriptionPaymentMethod.Paddle, providerReference);
    }

    public (WalletProductDefinitionOptions Product, string ProviderReference, decimal Amount) PrepareAfdianGoldsResupplyPack(
        string? providerReference = null
    )
    {
        return ResolveProductReference(SubscriptionPaymentMethod.Afdian, providerReference);
    }

    public bool IsGoldCurrencyPurchase(ISubscriptionOrder providerOrder)
    {
        try
        {
            ResolveProductReference(providerOrder.Provider, providerOrder.SubscriptionId);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<SnWalletOrder> CreateOrApplyGoldsResupplyPackPurchaseAsync(
        ISubscriptionOrder providerOrder,
        CancellationToken cancellationToken = default
    )
    {
        var (product, providerReference, amount) = ResolveProductReference(providerOrder.Provider, providerOrder.SubscriptionId);
        var accountId = await ResolveAccountIdForOrderAsync(providerOrder, cancellationToken);
        var wallet = await wallets.GetAccountWalletAsync(accountId) ?? await wallets.CreateWalletAsync(accountId: accountId);
        var orderRemark = BuildExternalOrderRemark(product.Identifier, providerOrder.Provider, providerOrder.Id);

        var existingOrder = await db.PaymentOrders
            .Include(o => o.Transaction)
            .FirstOrDefaultAsync(
                o => o.AppIdentifier == SnWalletOrder.InternalAppIdentifier &&
                     o.ProductIdentifier == product.Identifier &&
                     o.Remarks == orderRemark,
                cancellationToken
            );

        if (existingOrder is not null)
            return await ApplyPaidWalletProductOrderAsync(existingOrder, cancellationToken);

        var order = await payment.CreateOrderAsync(
            wallet.Id,
            product.Currency,
            amount,
            appIdentifier: SnWalletOrder.InternalAppIdentifier,
            productIdentifier: product.Identifier,
            remarks: orderRemark,
            meta: new Dictionary<string, object>
            {
                ["wallet_product"] = product.Identifier,
                ["wallet_product_key"] = GoldsResupplyPackKey,
                ["provider"] = providerOrder.Provider,
                ["provider_order_id"] = providerOrder.Id,
                ["provider_reference_id"] = providerReference,
                ["account_id"] = accountId.ToString(),
                ["display_name"] = product.DisplayName
            },
            reuseable: false
        );

        order.Status = OrderStatus.Paid;
        await db.SaveChangesAsync(cancellationToken);

        return await ApplyPaidWalletProductOrderAsync(order, cancellationToken);
    }

    public async Task<SnWalletOrder> ApplyPaidWalletProductOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default
    )
    {
        var order = await db.PaymentOrders
            .Include(o => o.Transaction)
            .Include(o => o.PayeeWallet)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Wallet product order not found.");

        return await ApplyPaidWalletProductOrderAsync(order, cancellationToken);
    }

    public async Task<SnWalletOrder> ApplyPaidWalletProductOrderAsync(
        SnWalletOrder order,
        CancellationToken cancellationToken = default
    )
    {
        var product = GetGoldsResupplyPackDefinition();
        if (!string.Equals(order.ProductIdentifier, product.Identifier, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported wallet product order.");
        if (order.Status == OrderStatus.Finished && order.TransactionId.HasValue)
            return order;
        if (order.Status != OrderStatus.Paid)
            throw new InvalidOperationException("Wallet product order must be paid before it can be applied.");
        if (!order.PayeeWalletId.HasValue)
            throw new InvalidOperationException("Wallet product order is missing a payee wallet.");

        if (!order.TransactionId.HasValue)
        {
            var transaction = await payment.CreateTransactionAsync(
                payerWalletId: null,
                payeeWalletId: order.PayeeWalletId.Value,
                currency: order.Currency,
                amount: order.Amount,
                remarks: $"{product.DisplayName} deposit",
                type: TransactionType.System,
                silent: true
            );

            order.TransactionId = transaction.Id;
            order.Transaction = transaction;
        }

        order.Status = OrderStatus.Finished;
        await db.SaveChangesAsync(cancellationToken);

        await NotifyWalletProductAppliedAsync(order, product, cancellationToken);
        return order;
    }

    private (WalletProductDefinitionOptions Product, string ProviderReference, decimal Amount) ResolveProductReference(
        string provider,
        string? providerReference
    )
    {
        var product = GetGoldsResupplyPackDefinition();
        var mappings = GetProviderMappings(product, provider);
        if (mappings.Count == 0)
            throw new InvalidOperationException($"No {provider} mapping was configured for {product.DisplayName}.");

        if (!string.IsNullOrWhiteSpace(providerReference))
        {
            var match = mappings.FirstOrDefault(x => string.Equals(x.Key, providerReference, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Key))
                return (product, match.Key, match.Value);

            throw new InvalidOperationException(
                $"Provider reference {providerReference} was not configured for {product.DisplayName} on {provider}."
            );
        }

        var defaultMapping = mappings.First();
        return (product, defaultMapping.Key, defaultMapping.Value);
    }

    private Dictionary<string, decimal> GetProviderMappings(WalletProductDefinitionOptions product, string provider)
    {
        var mapping = product.ProviderMappings
            .FirstOrDefault(x => string.Equals(x.Key, provider, StringComparison.OrdinalIgnoreCase))
            .Value;

        return mapping ?? [];
    }

    private async Task<Guid> ResolveAccountIdForOrderAsync(ISubscriptionOrder order, CancellationToken cancellationToken)
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
                    cancellationToken: cancellationToken
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

        throw new InvalidOperationException("Unable to resolve the account for this wallet product purchase.");
    }

    private async Task NotifyWalletProductAppliedAsync(
        SnWalletOrder order,
        WalletProductDefinitionOptions product,
        CancellationToken cancellationToken
    )
    {
        var wallet = order.PayeeWallet;
        if (wallet is null && order.PayeeWalletId.HasValue)
        {
            wallet = await db.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == order.PayeeWalletId.Value, cancellationToken);
        }
        if (wallet?.AccountId is not Guid accountId)
            return;

        var accountInfo = await remoteAccounts.GetAccount(accountId);
        var locale = accountInfo.Language;
        var pocket = await db.WalletPockets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.WalletId == order.PayeeWalletId!.Value && p.Currency == order.Currency,
                cancellationToken
            );

        var balance = pocket?.Amount ?? order.Amount;
        await pusher.SendPushNotificationToUserAsync(
            new DySendPushNotificationToUserRequest
            {
                UserId = accountId.ToString(),
                Notification = new DyPushNotification
                {
                    Topic = "wallets.products.applied",
                    Title = localizer.Get("walletProductAppliedTitle", locale: locale),
                    Subtitle = product.DisplayName,
                    Body = localizer.Get(
                        "walletProductAppliedBody",
                        locale: locale,
                        args: new
                        {
                            product = product.DisplayName,
                            amount = order.Amount.ToString(CultureInfo.InvariantCulture),
                            currency = GetTranslationForCurrency(order.Currency, locale),
                            balance = balance.ToString(CultureInfo.InvariantCulture)
                        }
                    ),
                    Meta = InfraObjectCoder.ConvertObjectToByteString(
                        new Dictionary<string, object>
                        {
                            ["order_id"] = order.Id.ToString(),
                            ["product_identifier"] = product.Identifier,
                            ["amount"] = order.Amount,
                            ["currency"] = order.Currency,
                            ["balance"] = balance
                        }
                    ),
                    IsSavable = true
                }
            }
        );
    }

    private string GetTranslationForCurrency(string currency, string locale)
    {
        return currency switch
        {
            WalletCurrency.SourcePoint => localizer.Get("currencyPoints", locale),
            WalletCurrency.GoldenPoint => localizer.Get("currencyGolds", locale),
            _ => currency
        };
    }

    private static string BuildExternalOrderRemark(string productIdentifier, string provider, string orderId)
    {
        return $"wallet-product:{productIdentifier}:{provider}:{orderId}";
    }
}
