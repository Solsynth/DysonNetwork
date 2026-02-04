using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace DysonNetwork.Shared.Registry;

public class RemotePaymentService(DysonNetwork.Shared.Proto.PaymentService.PaymentServiceClient payment)
{
    public async Task<DysonNetwork.Shared.Proto.Order> CreateOrder(
        string currency,
        string amount,
        string? payeeWalletId = null,
        TimeSpan? expiration = null,
        string? appIdentifier = null,
        string? productIdentifier = null,
        byte[]? meta = null,
        string? remarks = null,
        bool reuseable = false)
    {
        var request = new DysonNetwork.Shared.Proto.CreateOrderRequest
        {
            Currency = currency,
            Amount = amount,
            Reuseable = reuseable
        };

        if (payeeWalletId != null)
            request.PayeeWalletId = payeeWalletId;

        if (expiration.HasValue)
            request.Expiration = Duration.FromTimeSpan(expiration.Value);

        if (appIdentifier != null)
            request.AppIdentifier = appIdentifier;

        if (productIdentifier != null)
            request.ProductIdentifier = productIdentifier;

        if (meta != null)
            request.Meta = ByteString.CopyFrom(meta);

        if (remarks != null)
            request.Remarks = remarks;

        var response = await payment.CreateOrderAsync(request);
        return response;
    }

    public async Task<DysonNetwork.Shared.Proto.Transaction> CreateTransaction(
        string? payerWalletId,
        string? payeeWalletId,
        string currency,
        string amount,
        string? remarks = null,
        DysonNetwork.Shared.Proto.TransactionType type = DysonNetwork.Shared.Proto.TransactionType.Unspecified)
    {
        var request = new DysonNetwork.Shared.Proto.CreateTransactionRequest
        {
            Currency = currency,
            Amount = amount,
            Type = type
        };

        if (payerWalletId != null)
            request.PayerWalletId = payerWalletId;

        if (payeeWalletId != null)
            request.PayeeWalletId = payeeWalletId;

        if (remarks != null)
            request.Remarks = remarks;

        var response = await payment.CreateTransactionAsync(request);
        return response;
    }

    public async Task<DysonNetwork.Shared.Proto.Transaction> CreateTransactionWithAccount(
        string? payerAccountId,
        string? payeeAccountId,
        string currency,
        string amount,
        string? remarks = null,
        DysonNetwork.Shared.Proto.TransactionType type = DysonNetwork.Shared.Proto.TransactionType.Unspecified)
    {
        var request = new DysonNetwork.Shared.Proto.CreateTransactionWithAccountRequest
        {
            Currency = currency,
            Amount = amount,
            Type = type
        };

        if (payerAccountId != null)
            request.PayerAccountId = payerAccountId;

        if (payeeAccountId != null)
            request.PayeeAccountId = payeeAccountId;

        if (remarks != null)
            request.Remarks = remarks;

        var response = await payment.CreateTransactionWithAccountAsync(request);
        return response;
    }

    public async Task<DysonNetwork.Shared.Proto.Transaction> Transfer(
        Guid payerAccountId,
        Guid payeeAccountId,
        string currency,
        string amount)
    {
        var request = new DysonNetwork.Shared.Proto.TransferRequest
        {
            PayerAccountId = payerAccountId.ToString(),
            PayeeAccountId = payeeAccountId.ToString(),
            Currency = currency,
            Amount = amount
        };

        var response = await payment.TransferAsync(request);
        return response;
    }

    public async Task<DysonNetwork.Shared.Proto.Order> CancelOrder(string orderId)
    {
        var request = new DysonNetwork.Shared.Proto.CancelOrderRequest { OrderId = orderId };
        var response = await payment.CancelOrderAsync(request);
        return response;
    }

    public async Task<DysonNetwork.Shared.Proto.RefundOrderResponse> RefundOrder(string orderId)
    {
        var request = new DysonNetwork.Shared.Proto.RefundOrderRequest { OrderId = orderId };
        var response = await payment.RefundOrderAsync(request);
        return response;
    }

    public async Task<WalletFund> GetWalletFund(string fundId)
    {
        var request = new GetWalletFundRequest { FundId = fundId };
        var response = await payment.GetWalletFundAsync(request);
        return response;
    }
}
