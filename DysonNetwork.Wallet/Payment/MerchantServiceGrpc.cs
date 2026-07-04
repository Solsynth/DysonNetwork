using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Wallet.Payment;

public class MerchantServiceGrpc(
    MerchantService merchantService,
    WalletService walletService,
    PaymentService paymentService,
    AppDatabase db
) : DyMerchantService.DyMerchantServiceBase
{
    public override async Task<DyMerchant> UpsertMerchant(
        DyUpsertMerchantRequest request,
        ServerCallContext context)
    {
        var merchant = await merchantService.UpsertMerchantAsync(
            Guid.Parse(request.PublisherId),
            request.HasPaymentWalletId ? Guid.Parse(request.PaymentWalletId) : null,
            request.HasName ? request.Name : null);

        return new DyMerchant
        {
            Id = merchant.Id.ToString(),
            PublisherId = merchant.PublisherId.ToString(),
            PaymentWalletId = merchant.PaymentWalletId?.ToString(),
            Name = merchant.Name ?? string.Empty
        };
    }

    public override async Task<DyMerchantSettlement> CreateMerchantSettlement(
        DyCreateMerchantSettlementRequest request,
        ServerCallContext context)
    {
        var publisherId = Guid.Parse(request.PublisherId);

        var merchant = await db.Merchants
            .FirstOrDefaultAsync(m => m.PublisherId == publisherId);

        if (merchant == null)
        {
            // Auto-create merchant
            merchant = new SnMerchant { PublisherId = publisherId };
            db.Merchants.Add(merchant);
            await db.SaveChangesAsync();
        }

        if (!merchant.PaymentWalletId.HasValue)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Merchant has no payment wallet configured"));

        var settlement = await merchantService.CreateSettlementAsync(
            merchant.Id,
            paymentTransactionId: null,
            paymentWalletId: merchant.PaymentWalletId.Value,
            currency: request.Currency,
            amount: decimal.Parse(request.Amount),
            orderId: request.HasOrderId ? Guid.Parse(request.OrderId) : null,
            awardId: request.HasAwardId ? Guid.Parse(request.AwardId) : null);

        return ToProto(settlement);
    }

    public override async Task<DyGetPendingSettlementsResponse> GetPendingSettlements(
        DyGetPendingSettlementsRequest request,
        ServerCallContext context)
    {
        var walletId = Guid.Parse(request.PaymentWalletId);
        var pending = await merchantService.GetPendingSettlementsByWalletAsync(walletId);

        return new DyGetPendingSettlementsResponse
        {
            Settlements = { pending.Select(ToProto) }
        };
    }

    public override async Task<DySettleMerchantResponse> SettleMerchant(
        DySettleMerchantRequest request,
        ServerCallContext context)
    {
        var walletId = Guid.Parse(request.PaymentWalletId);
        var transactions = await merchantService.SettleWalletAsync(
            walletId,
            MerchantSettlementTrigger.Manual,
            walletService,
            paymentService);

        return new DySettleMerchantResponse
        {
            Transactions = { transactions.Select(t => t.ToProtoValue()) }
        };
    }

    private static DyMerchantSettlement ToProto(SnMerchantSettlement s) => new()
    {
        Id = s.Id.ToString(),
        MerchantId = s.MerchantId.ToString(),
        OrderId = s.OrderId?.ToString(),
        AwardId = s.AwardId?.ToString(),
        PaymentWalletId = s.PaymentWalletId.ToString(),
        Currency = s.Currency,
        Amount = s.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Status = s.Status switch
        {
            MerchantSettlementStatus.Pending => DyMerchantSettlementStatus.Pending,
            MerchantSettlementStatus.Settled => DyMerchantSettlementStatus.Settled,
            MerchantSettlementStatus.Cancelled => DyMerchantSettlementStatus.Cancelled,
            _ => DyMerchantSettlementStatus.Unspecified
        },
        SettledBy = s.SettledBy switch
        {
            MerchantSettlementTrigger.Automatic => DyMerchantSettlementTrigger.Automatic,
            MerchantSettlementTrigger.Manual => DyMerchantSettlementTrigger.Manual,
            _ => DyMerchantSettlementTrigger.Unspecified
        },
        SettledAt = s.SettledAt.HasValue
            ? Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(s.SettledAt.Value.ToDateTimeOffset())
            : null
    };
}
