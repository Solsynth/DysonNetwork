using DysonNetwork.Shared.Models;
using NodaTime;
using Microsoft.EntityFrameworkCore;


namespace DysonNetwork.Wallet.Payment;

public sealed class BillingRecordResponse
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = null!;
    public string ExternalId { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public string? ProviderReferenceId { get; set; }
    public string? ProductIdentifier { get; set; }
    public string? AccountIdentifier { get; set; }
    public Instant BegunAt { get; set; }
    public Duration Duration { get; set; }
    public bool IsTesting { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
    public List<BillingOrderSummary> Orders { get; set; } = [];
    public List<BillingSubscriptionSummary> Subscriptions { get; set; } = [];

    public static BillingRecordResponse FromEntity(
        SnWalletInboundOrder inboundOrder,
        bool includeAccountIdentifier = true
    ) => new()
    {
        Id = inboundOrder.Id,
        Provider = inboundOrder.Provider,
        ExternalId = inboundOrder.ExternalId,
        CorrelationId = inboundOrder.CorrelationId,
        ProviderReferenceId = inboundOrder.ProviderReferenceId,
        ProductIdentifier = inboundOrder.ProductIdentifier,
        AccountIdentifier = includeAccountIdentifier ? inboundOrder.AccountIdentifier : null,
        BegunAt = inboundOrder.BegunAt,
        Duration = inboundOrder.Duration,
        IsTesting = inboundOrder.IsTesting,
        CreatedAt = inboundOrder.CreatedAt,
        UpdatedAt = inboundOrder.UpdatedAt,
        Orders = inboundOrder.WalletOrders
            .OrderByDescending(x => x.CreatedAt)
            .Select(BillingOrderSummary.FromEntity)
            .ToList(),
        Subscriptions = inboundOrder.WalletSubscriptions
            .OrderByDescending(x => x.BegunAt)
            .Select(BillingSubscriptionSummary.FromEntity)
            .ToList()
    };
}

public sealed class BillingOrderSummary
{
    public Guid Id { get; set; }
    public int Status { get; set; }
    public string Currency { get; set; } = null!;
    public string? Remarks { get; set; }
    public string? AppIdentifier { get; set; }
    public string? ProductIdentifier { get; set; }
    public decimal Amount { get; set; }
    public Instant ExpiredAt { get; set; }
    public Guid? PayeeWalletId { get; set; }
    public Guid? TransactionId { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    public static BillingOrderSummary FromEntity(SnWalletOrder order) => new()
    {
        Id = order.Id,
        Status = (int)order.Status,
        Currency = order.Currency,
        Remarks = order.Remarks,
        AppIdentifier = order.AppIdentifier,
        ProductIdentifier = order.ProductIdentifier,
        Amount = order.Amount,
        ExpiredAt = order.ExpiredAt,
        PayeeWalletId = order.PayeeWalletId,
        TransactionId = order.TransactionId,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt
    };
}

public sealed class BillingSubscriptionSummary
{
    public Guid Id { get; set; }
    public Instant BegunAt { get; set; }
    public Instant? EndedAt { get; set; }
    public string Identifier { get; set; } = null!;
    public string? GroupIdentifier { get; set; }
    public bool IsActive { get; set; }
    public bool IsFreeTrial { get; set; }
    public int Status { get; set; }
    public string? PaymentMethod { get; set; }
    public decimal? BasePrice { get; set; }
    public Instant? RenewalAt { get; set; }
    public bool IsTesting { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }

    public static BillingSubscriptionSummary FromEntity(SnWalletSubscription subscription) => new()
    {
        Id = subscription.Id,
        BegunAt = subscription.BegunAt,
        EndedAt = subscription.EndedAt,
        Identifier = subscription.Identifier,
        GroupIdentifier = subscription.GroupIdentifier,
        IsActive = subscription.IsActive,
        IsFreeTrial = subscription.IsFreeTrial,
        Status = (int)subscription.Status,
        PaymentMethod = subscription.PaymentMethod,
        BasePrice = subscription.BasePrice,
        RenewalAt = subscription.RenewalAt,
        IsTesting = subscription.IsTesting,
        CreatedAt = subscription.CreatedAt,
        UpdatedAt = subscription.UpdatedAt
    };
}

public static class BillingRecordQueryExtensions
{
    public static IQueryable<SnWalletInboundOrder> IncludeBillingRelations(
        this IQueryable<SnWalletInboundOrder> query
    )
    {
        return query
            .Include(x => x.WalletOrders)
                .ThenInclude(x => x.PayeeWallet)
            .Include(x => x.WalletOrders)
                .ThenInclude(x => x.Transaction)
                    .ThenInclude(x => x!.PayerWallet)
            .Include(x => x.WalletOrders)
                .ThenInclude(x => x.Transaction)
                    .ThenInclude(x => x!.PayeeWallet)
            .Include(x => x.WalletSubscriptions);
    }
}
