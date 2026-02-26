using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace DysonNetwork.Shared.Registry;

public class RemotePaymentService(DyPaymentService.DyPaymentServiceClient payment)
{
    public async Task<DyOrder> CreateOrder(
        string currency,
        string amount,
        string? payeeWalletId = null,
        TimeSpan? expiration = null,
        string? appIdentifier = null,
        string? productIdentifier = null,
        byte[]? meta = null,
        string? remarks = null,
        bool reuseable = false
    )
    {
        var request = new DyCreateOrderRequest
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

    public async Task<DyTransaction> CreateTransaction(
        string? payerWalletId,
        string? payeeWalletId,
        string currency,
        string amount,
        string? remarks = null,
        DyTransactionType type = DyTransactionType.Unspecified)
    {
        var request = new DysonNetwork.Shared.Proto.DyCreateTransactionRequest
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

    public async Task<DyTransaction> CreateTransactionWithAccount(
        string? payerAccountId,
        string? payeeAccountId,
        string currency,
        string amount,
        string? remarks = null,
        DyTransactionType type = DyTransactionType.Unspecified)
    {
        var request = new DyCreateTransactionWithAccountRequest
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

    public async Task<DyTransaction> Transfer(
        Guid payerAccountId,
        Guid payeeAccountId,
        string currency,
        string amount)
    {
        var request = new DysonNetwork.Shared.Proto.DyTransferRequest
        {
            PayerAccountId = payerAccountId.ToString(),
            PayeeAccountId = payeeAccountId.ToString(),
            Currency = currency,
            Amount = amount
        };

        var response = await payment.TransferAsync(request);
        return response;
    }

    public async Task<DyOrder> CancelOrder(string orderId)
    {
        var request = new DysonNetwork.Shared.Proto.DyCancelOrderRequest { OrderId = orderId };
        var response = await payment.CancelOrderAsync(request);
        return response;
    }

    public async Task<DysonNetwork.Shared.Proto.DyRefundOrderResponse> RefundOrder(string orderId)
    {
        var request = new DysonNetwork.Shared.Proto.DyRefundOrderRequest { OrderId = orderId };
        var response = await payment.RefundOrderAsync(request);
        return response;
    }

    public async Task<DyWalletFund> GetWalletFund(string fundId)
    {
        var request = new DysonNetwork.Shared.Proto.DyGetWalletFundRequest { FundId = fundId };
        var response = await payment.GetWalletFundAsync(request);
        return response;
    }
}