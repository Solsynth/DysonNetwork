using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

public class PaymentServiceGrpc(
    PaymentService paymentService,
    WalletService walletService,
    SubscriptionCatalogService catalogService,
    AppDatabase db,
    DyCustomAppService.DyCustomAppServiceClient customApps
) : DyPaymentService.DyPaymentServiceBase
{
    public override async Task<DyOrder> CreateOrder(
        DyCreateOrderRequest request,
        ServerCallContext context
    )
    {
        var items = request.Items.Count > 0
            ? request.Items.Select(SnWalletOrderItem.FromProto).ToList()
            : null;

        var appIdentifier = request.HasAppIdentifier ? request.AppIdentifier : SnWalletOrder.InternalAppIdentifier;
        var payeeWalletId = request.HasPayeeWalletId
            ? Guid.Parse(request.PayeeWalletId)
            : await ResolveMerchantWalletIdAsync(appIdentifier);

        var order = await paymentService.CreateOrderAsync(
            payeeWalletId,
            request.Currency,
            decimal.Parse(request.Amount),
            request.Expiration is not null
                ? Duration.FromSeconds(request.Expiration.Seconds)
                : null,
            appIdentifier,
            request.HasProductIdentifier ? request.ProductIdentifier : null,
            request.HasRemarks ? request.Remarks : null,
            request.HasMeta
                ? InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object>>(request.Meta)
                : null,
            request.Reuseable,
            items
        );
        return order.ToProtoValue();
    }

    private async Task<Guid?> ResolveMerchantWalletIdAsync(string appIdentifier)
    {
        const string prefix = "developer.app:";
        if (!appIdentifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(appIdentifier[prefix.Length..], out var appId))
            return null;

        try
        {
            var developer = await customApps.GetAppDeveloperAsync(new DyGetAppDeveloperRequest
            {
                AppId = appId.ToString()
            });
            if (!Guid.TryParse(developer.Developer.PublisherId, out var publisherId))
                return null;

            var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.PublisherId == publisherId);
            return merchant?.PaymentWalletId;
        }
        catch (RpcException)
        {
            return null;
        }
    }

    public override async Task<DyTransaction> CreateTransactionWithAccount(
        DyCreateTransactionWithAccountRequest request,
        ServerCallContext context
    )
    {
        var transaction = await paymentService.CreateTransactionWithAccountAsync(
            request.PayerAccountId is not null ? Guid.Parse(request.PayerAccountId) : null,
            request.PayeeAccountId is not null ? Guid.Parse(request.PayeeAccountId) : null,
            request.Currency,
            decimal.Parse(request.Amount),
            request.Remarks is not null ? request.Remarks : null,
            (Shared.Models.TransactionType)request.Type
        );
        return transaction.ToProtoValue();
    }

    public override async Task<DyTransaction> CreateTransaction(
        DyCreateTransactionRequest request,
        ServerCallContext context
    )
    {
        var transaction = await paymentService.CreateTransactionAsync(
            request.PayerWalletId is not null ? Guid.Parse(request.PayerWalletId) : null,
            request.PayeeWalletId is not null ? Guid.Parse(request.PayeeWalletId) : null,
            request.Currency,
            decimal.Parse(request.Amount),
            request.Remarks is not null ? request.Remarks : null,
            (Shared.Models.TransactionType)request.Type
        );
        return transaction.ToProtoValue();
    }

    public override async Task<DyOrder> CancelOrder(
        DyCancelOrderRequest request,
        ServerCallContext context
    )
    {
        var order = await paymentService.CancelOrderAsync(Guid.Parse(request.OrderId));
        return order.ToProtoValue();
    }

    public override async Task<DyRefundOrderResponse> RefundOrder(
        DyRefundOrderRequest request,
        ServerCallContext context
    )
    {
        var (order, refundTransaction) = await paymentService.RefundOrderAsync(
            Guid.Parse(request.OrderId)
        );
        return new DyRefundOrderResponse
        {
            Order = order.ToProtoValue(),
            RefundTransaction = refundTransaction.ToProtoValue(),
        };
    }

    public override async Task<DyTransaction> Transfer(
        DyTransferRequest request,
        ServerCallContext context
    )
    {
        SnWalletTransaction transaction;

        if (!string.IsNullOrWhiteSpace(request.PayerWalletId) && !string.IsNullOrWhiteSpace(request.PayeeWalletId))
        {
            transaction = await paymentService.TransferBetweenWalletsAsync(
                Guid.Parse(request.PayerWalletId),
                Guid.Parse(request.PayeeWalletId),
                request.Currency,
                decimal.Parse(request.Amount),
                string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks
            );
        }
        else if (!string.IsNullOrWhiteSpace(request.PayerWalletId) && !string.IsNullOrWhiteSpace(request.PayeePublicId))
        {
            var payeeWallet = await walletService.GetWalletByPublicIdAsync(request.PayeePublicId)
                ?? throw new RpcException(new Status(StatusCode.NotFound, "Payee wallet not found."));

            transaction = await paymentService.TransferBetweenWalletsAsync(
                Guid.Parse(request.PayerWalletId),
                payeeWallet.Id,
                request.Currency,
                decimal.Parse(request.Amount),
                string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks
            );
        }
        else if (!string.IsNullOrWhiteSpace(request.PayerAccountId) && !string.IsNullOrWhiteSpace(request.PayeeAccountId))
        {
            transaction = await paymentService.TransferAsync(
                Guid.Parse(request.PayerAccountId),
                Guid.Parse(request.PayeeAccountId),
                request.Currency,
                decimal.Parse(request.Amount),
                string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks
            );
        }
        else
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                "Either payer_account_id/payee_account_id, payer_wallet_id/payee_wallet_id, or payer_wallet_id/payee_public_id is required."));
        }

        return transaction.ToProtoValue();
    }

    public override async Task<DyWalletFund> GetWalletFund(
        DyGetWalletFundRequest request,
        ServerCallContext context
    )
    {
        var walletFund = await paymentService.GetWalletFundAsync(Guid.Parse(request.FundId));
        return walletFund?.ToProtoValueWithRecipients() ?? new DyWalletFund();
    }

    public override async Task<DyRegisterAppSubscriptionDefinitionResponse> RegisterAppSubscriptionDefinition(
        DyRegisterAppSubscriptionDefinitionRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.AppIdentifier))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "identifier and app_identifier required"));

        if (request.Remove)
        {
            var removed = await catalogService.RemoveAppDefinitionAsync(request.Identifier, request.AppIdentifier);
            return new DyRegisterAppSubscriptionDefinitionResponse { Created = false };
        }

        var created = await catalogService.RegisterAppDefinitionAsync(
            request.Identifier,
            request.AppIdentifier,
            request.DisplayName,
            request.Currency,
            decimal.Parse(request.BasePrice),
            string.IsNullOrWhiteSpace(request.GroupIdentifier) ? null : request.GroupIdentifier,
            request.CycleDurationDays
        );

        return new DyRegisterAppSubscriptionDefinitionResponse { Created = created };
    }
}
