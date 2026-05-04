using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

public class PaymentServiceGrpc(PaymentService paymentService, WalletService walletService)
    : DyPaymentService.DyPaymentServiceBase
{
    public override async Task<DyOrder> CreateOrder(
        DyCreateOrderRequest request,
        ServerCallContext context
    )
    {
        var order = await paymentService.CreateOrderAsync(
            request.HasPayeeWalletId ? Guid.Parse(request.PayeeWalletId) : null,
            request.Currency,
            decimal.Parse(request.Amount),
            request.Expiration is not null
                ? Duration.FromSeconds(request.Expiration.Seconds)
                : null,
            request.HasAppIdentifier ? request.AppIdentifier : SnWalletOrder.InternalAppIdentifier,
            request.HasProductIdentifier ? request.ProductIdentifier : null,
            request.HasRemarks ? request.Remarks : null,
            request.HasMeta
                ? InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, object>>(request.Meta)
                : null,
            request.Reuseable
        );
        return order.ToProtoValue();
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
}
