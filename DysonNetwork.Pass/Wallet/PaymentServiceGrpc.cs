using DysonNetwork.Shared.Proto;
using Grpc.Core;
using NodaTime;

namespace DysonNetwork.Pass.Wallet;

public class PaymentServiceGrpc(PaymentService paymentService) : Shared.Proto.PaymentService.PaymentServiceBase
{
    public override async Task<Shared.Proto.Order> CreateOrder(CreateOrderRequest request, ServerCallContext context)
    {
        var order = await paymentService.CreateOrderAsync(
            request.HasPayeeWalletId ? Guid.Parse(request.PayeeWalletId) : null,
            request.Currency,
            decimal.Parse(request.Amount),
            request.Expiration is not null ? Duration.FromSeconds(request.Expiration.Seconds) : null,
            request.HasAppIdentifier ? request.AppIdentifier : Order.InternalAppIdentifier,
            request.HasProductIdentifier ? request.ProductIdentifier : null,
            request.HasMeta
                ? GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object>>(request.Meta)
                : null,
            request.Reuseable
        );
        return order.ToProtoValue();
    }

    public override async Task<Shared.Proto.Transaction> CreateTransactionWithAccount(
        CreateTransactionWithAccountRequest request, ServerCallContext context)
    {
        var transaction = await paymentService.CreateTransactionWithAccountAsync(
            request.HasPayerAccountId ? Guid.Parse(request.PayerAccountId) : null,
            request.HasPayeeAccountId ? Guid.Parse(request.PayeeAccountId) : null,
            request.Currency,
            decimal.Parse(request.Amount),
            request.HasRemarks ? request.Remarks : null,
            (TransactionType)request.Type
        );
        return transaction.ToProtoValue();
    }

    public override async Task<Shared.Proto.Transaction> CreateTransaction(CreateTransactionRequest request,
        ServerCallContext context)
    {
        var transaction = await paymentService.CreateTransactionAsync(
            request.HasPayerWalletId ? Guid.Parse(request.PayerWalletId) : null,
            request.HasPayeeWalletId ? Guid.Parse(request.PayeeWalletId) : null,
            request.Currency,
            decimal.Parse(request.Amount),
            request.HasRemarks ? request.Remarks : null,
            (TransactionType)request.Type
        );
        return transaction.ToProtoValue();
    }

    public override async Task<Shared.Proto.Order> CancelOrder(CancelOrderRequest request, ServerCallContext context)
    {
        var order = await paymentService.CancelOrderAsync(Guid.Parse(request.OrderId));
        return order.ToProtoValue();
    }

    public override async Task<RefundOrderResponse> RefundOrder(RefundOrderRequest request, ServerCallContext context)
    {
        var (order, refundTransaction) = await paymentService.RefundOrderAsync(Guid.Parse(request.OrderId));
        return new RefundOrderResponse
        {
            Order = order.ToProtoValue(),
            RefundTransaction = refundTransaction.ToProtoValue()
        };
    }

    public override async Task<Shared.Proto.Transaction> Transfer(TransferRequest request, ServerCallContext context)
    {
        var transaction = await paymentService.TransferAsync(
            Guid.Parse(request.PayerAccountId),
            Guid.Parse(request.PayeeAccountId),
            request.Currency,
            decimal.Parse(request.Amount)
        );
        return transaction.ToProtoValue();
    }
}