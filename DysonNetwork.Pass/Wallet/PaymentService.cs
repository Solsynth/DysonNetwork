using System.Globalization;
using DysonNetwork.Pass.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Stream;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using NATS.Client.Core;
using NATS.Net;
using NodaTime;
using AccountService = DysonNetwork.Pass.Account.AccountService;

namespace DysonNetwork.Pass.Wallet;

public class PaymentService(
    AppDatabase db,
    WalletService wat,
    RingService.RingServiceClient pusher,
    IStringLocalizer<NotificationResource> localizer,
    INatsConnection nats
)
{
    public async Task<SnWalletOrder> CreateOrderAsync(
        Guid? payeeWalletId,
        string currency,
        decimal amount,
        Duration? expiration = null,
        string? appIdentifier = null,
        string? productIdentifier = null,
        string? remarks = null,
        Dictionary<string, object>? meta = null,
        bool reuseable = true
    )
    {
        // Check if there's an existing unpaid order that can be reused
        var now = SystemClock.Instance.GetCurrentInstant();
        if (reuseable && appIdentifier != null)
        {
            var existingOrder = await db.PaymentOrders
                .Where(o => o.Status == Shared.Models.OrderStatus.Unpaid &&
                            o.PayeeWalletId == payeeWalletId &&
                            o.Currency == currency &&
                            o.Amount == amount &&
                            o.AppIdentifier == appIdentifier &&
                            o.ProductIdentifier == productIdentifier &&
                            o.ExpiredAt > now)
                .FirstOrDefaultAsync();

            // If an existing order is found, check if meta matches
            if (existingOrder != null && meta != null && existingOrder.Meta != null)
            {
                // Compare the meta dictionary - if they are equivalent, reuse the order
                var metaMatches = existingOrder.Meta.Count == meta.Count &&
                                  !existingOrder.Meta.Except(meta).Any();
                if (metaMatches)
                    return existingOrder;
            }
        }

        // Create a new SnWalletOrder if no reusable order was found
        var order = new SnWalletOrder
        {
            PayeeWalletId = payeeWalletId,
            Currency = currency,
            Amount = amount,
            ExpiredAt = now.Plus(expiration ?? Duration.FromHours(24)),
            AppIdentifier = appIdentifier,
            ProductIdentifier = productIdentifier,
            Remarks = remarks,
            Meta = meta
        };

        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    public async Task<SnWalletTransaction> CreateTransactionWithAccountAsync(
        Guid? payerAccountId,
        Guid? payeeAccountId,
        string currency,
        decimal amount,
        string? remarks = null,
        Shared.Models.TransactionType type = Shared.Models.TransactionType.System
    )
    {
        SnWallet? payer = null, payee = null;
        if (payerAccountId.HasValue)
            payer = await db.Wallets.FirstOrDefaultAsync(e => e.AccountId == payerAccountId.Value);
        if (payeeAccountId.HasValue)
            payee = await db.Wallets.FirstOrDefaultAsync(e => e.AccountId == payeeAccountId.Value);

        if (payer == null && payerAccountId.HasValue)
            throw new ArgumentException("Payer account was specified, but wallet was not found");
        if (payee == null && payeeAccountId.HasValue)
            throw new ArgumentException("Payee account was specified, but wallet was not found");

        return await CreateTransactionAsync(
            payer?.Id,
            payee?.Id,
            currency,
            amount,
            remarks,
            type
        );
    }

    public async Task<SnWalletTransaction> CreateTransactionAsync(
        Guid? payerWalletId,
        Guid? payeeWalletId,
        string currency,
        decimal amount,
        string? remarks = null,
        Shared.Models.TransactionType type = Shared.Models.TransactionType.System,
        bool silent = false
    )
    {
        if (payerWalletId == null && payeeWalletId == null)
            throw new ArgumentException("At least one wallet must be specified.");
        if (amount <= 0) throw new ArgumentException("Cannot create transaction with negative or zero amount.");

        var transaction = new SnWalletTransaction
        {
            PayerWalletId = payerWalletId,
            PayeeWalletId = payeeWalletId,
            Currency = currency,
            Amount = amount,
            Remarks = remarks,
            Type = type
        };

        SnWallet? payerWallet = null, payeeWallet = null;

        if (payerWalletId.HasValue)
        {
            payerWallet = await db.Wallets.FirstOrDefaultAsync(e => e.AccountId == payerWalletId.Value);

            var (payerPocket, isNewlyCreated) =
                await wat.GetOrCreateWalletPocketAsync(payerWalletId.Value, currency);

            if (isNewlyCreated || payerPocket.Amount < amount)
                throw new InvalidOperationException("Insufficient funds");

            await db.WalletPockets
                .Where(p => p.Id == payerPocket.Id && p.Amount >= amount)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(p => p.Amount, p => p.Amount - amount));
        }

        if (payeeWalletId.HasValue)
        {
            payeeWallet = await db.Wallets.FirstOrDefaultAsync(e => e.AccountId == payeeWalletId.Value);

            var (payeePocket, isNewlyCreated) =
                await wat.GetOrCreateWalletPocketAsync(payeeWalletId.Value, currency, amount);

            if (!isNewlyCreated)
                await db.WalletPockets
                    .Where(p => p.Id == payeePocket.Id)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.Amount, p => p.Amount + amount));
        }

        db.PaymentTransactions.Add(transaction);
        await db.SaveChangesAsync();

        if (!silent)
            await NotifyNewTransaction(transaction, payerWallet, payeeWallet);

        return transaction;
    }

    private async Task NotifyNewTransaction(SnWalletTransaction transaction, SnWallet? payerWallet, SnWallet? payeeWallet)
    {
        if (payerWallet is not null)
        {
            var account = await db.Accounts
                .Where(a => a.Id == payerWallet.AccountId)
                .FirstOrDefaultAsync();
            if (account is null) return;

            AccountService.SetCultureInfo(account);

            // Due to ID is uuid, it longer than 8 words for sure
            var readableTransactionId = transaction.Id.ToString().Replace("-", "")[..8];
            var readableTransactionRemark = transaction.Remarks ?? $"#{readableTransactionId}";

            await pusher.SendPushNotificationToUserAsync(
                new SendPushNotificationToUserRequest
                {
                    UserId = account.Id.ToString(),
                    Notification = new PushNotification
                    {
                        Topic = "wallets.transactions",
                        Title = localizer["TransactionNewTitle", readableTransactionRemark],
                        Body = transaction.Amount > 0
                            ? localizer["TransactionNewBodyMinus",
                                transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                transaction.Currency]
                            : localizer["TransactionNewBodyPlus",
                                transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                transaction.Currency],
                        IsSavable = true
                    }
                }
            );
        }

        if (payeeWallet is not null)
        {
            var account = await db.Accounts
                .Where(a => a.Id == payeeWallet.AccountId)
                .FirstOrDefaultAsync();
            if (account is null) return;

            AccountService.SetCultureInfo(account);

            // Due to ID is uuid, it longer than 8 words for sure
            var readableTransactionId = transaction.Id.ToString().Replace("-", "")[..8];
            var readableTransactionRemark = transaction.Remarks ?? $"#{readableTransactionId}";

            await pusher.SendPushNotificationToUserAsync(
                new SendPushNotificationToUserRequest
                {
                    UserId = account.Id.ToString(),
                    Notification = new PushNotification
                    {
                        Topic = "wallets.transactions",
                        Title = localizer["TransactionNewTitle", readableTransactionRemark],
                        Body = transaction.Amount > 0
                            ? localizer["TransactionNewBodyPlus",
                                transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                transaction.Currency]
                            : localizer["TransactionNewBodyMinus",
                                transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                transaction.Currency],
                        IsSavable = true
                    }
                }
            );
        }
    }

    public async Task<SnWalletOrder> PayOrderAsync(Guid orderId, SnWallet payerWallet)
    {
        var order = await db.PaymentOrders
            .Include(o => o.Transaction)
            .Include(o => o.PayeeWallet)
            .FirstOrDefaultAsync(o => o.Id == orderId) ?? throw new InvalidOperationException("Order not found");
        var js = nats.CreateJetStreamContext();

        if (order.Status == Shared.Models.OrderStatus.Paid)
        {
            await js.PublishAsync(
                PaymentOrderEventBase.Type,
                GrpcTypeHelper.ConvertObjectToByteString(new PaymentOrderEvent
                {
                    OrderId = order.Id,
                    WalletId = payerWallet.Id,
                    AccountId = payerWallet.AccountId,
                    AppIdentifier = order.AppIdentifier,
                    ProductIdentifier = order.ProductIdentifier,
                    Meta = order.Meta ?? [],
                    Status = (int)order.Status,
                }).ToByteArray()
            );

            return order;
        }

        if (order.Status != Shared.Models.OrderStatus.Unpaid)
        {
            throw new InvalidOperationException($"Order is in invalid status: {order.Status}");
        }

        if (order.ExpiredAt < SystemClock.Instance.GetCurrentInstant())
        {
            order.Status = Shared.Models.OrderStatus.Expired;
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Order has expired");
        }

        var transaction = await CreateTransactionAsync(
            payerWallet.Id,
            order.PayeeWalletId,
            order.Currency,
            order.Amount,
            order.Remarks ?? $"Payment for Order #{order.Id}",
            type: Shared.Models.TransactionType.Order,
            silent: true);

        order.TransactionId = transaction.Id;
        order.Transaction = transaction;
        order.Status = Shared.Models.OrderStatus.Paid;

        await db.SaveChangesAsync();

        await NotifyOrderPaid(order, payerWallet, order.PayeeWallet);

        await js.PublishAsync(
            PaymentOrderEventBase.Type,
            GrpcTypeHelper.ConvertObjectToByteString(new PaymentOrderEvent
            {
                OrderId = order.Id,
                WalletId = payerWallet.Id,
                AccountId = payerWallet.AccountId,
                AppIdentifier = order.AppIdentifier,
                ProductIdentifier = order.ProductIdentifier,
                Meta = order.Meta ?? [],
                Status = (int)order.Status,
            }).ToByteArray()
        );

        return order;
    }

    private async Task NotifyOrderPaid(SnWalletOrder order, SnWallet? payerWallet, SnWallet? payeeWallet)
    {
        if (payerWallet is not null)
        {
            var account = await db.Accounts
                .Where(a => a.Id == payerWallet.AccountId)
                .FirstOrDefaultAsync();
            if (account is null) return;

            AccountService.SetCultureInfo(account);

            // Due to ID is uuid, it longer than 8 words for sure
            var readableOrderId = order.Id.ToString().Replace("-", "")[..8];
            var readableOrderRemark = order.Remarks ?? $"#{readableOrderId}";


            await pusher.SendPushNotificationToUserAsync(
                new SendPushNotificationToUserRequest
                {
                    UserId = account.Id.ToString(),
                    Notification = new PushNotification
                    {
                        Topic = "wallets.orders.paid",
                        Title = localizer["OrderPaidTitle", $"#{readableOrderId}"],
                        Body = localizer["OrderPaidBody", order.Amount.ToString(CultureInfo.InvariantCulture),
                            order.Currency,
                            readableOrderRemark],
                        IsSavable = true
                    }
                }
            );
        }

        if (payeeWallet is not null)
        {
            var account = await db.Accounts
                .Where(a => a.Id == payeeWallet.AccountId)
                .FirstOrDefaultAsync();
            if (account is null) return;

            AccountService.SetCultureInfo(account);

            // Due to ID is uuid, it longer than 8 words for sure
            var readableOrderId = order.Id.ToString().Replace("-", "")[..8];
            var readableOrderRemark = order.Remarks ?? $"#{readableOrderId}";

            await pusher.SendPushNotificationToUserAsync(
                new SendPushNotificationToUserRequest
                {
                    UserId = account.Id.ToString(),
                    Notification = new PushNotification
                    {
                        Topic = "wallets.orders.received",
                        Title = localizer["OrderReceivedTitle", $"#{readableOrderId}"],
                        Body = localizer["OrderReceivedBody", order.Amount.ToString(CultureInfo.InvariantCulture),
                            order.Currency,
                            readableOrderRemark],
                        IsSavable = true
                    }
                }
            );
        }
    }

    public async Task<SnWalletOrder> CancelOrderAsync(Guid orderId)
    {
        var order = await db.PaymentOrders.FindAsync(orderId);
        if (order == null)
        {
            throw new InvalidOperationException("Order not found");
        }

        if (order.Status != Shared.Models.OrderStatus.Unpaid)
        {
            throw new InvalidOperationException($"Cannot cancel order in status: {order.Status}");
        }

        order.Status = Shared.Models.OrderStatus.Cancelled;
        await db.SaveChangesAsync();
        return order;
    }

    public async Task<(SnWalletOrder Order, SnWalletTransaction RefundTransaction)> RefundOrderAsync(Guid orderId)
    {
        var order = await db.PaymentOrders
            .Include(o => o.Transaction)
            .FirstOrDefaultAsync(o => o.Id == orderId) ?? throw new InvalidOperationException("Order not found");
        if (order.Status != Shared.Models.OrderStatus.Paid)
        {
            throw new InvalidOperationException($"Cannot refund order in status: {order.Status}");
        }

        if (order.Transaction == null)
        {
            throw new InvalidOperationException("Order has no associated transaction");
        }

        var refundTransaction = await CreateTransactionAsync(
            order.PayeeWalletId,
            order.Transaction.PayerWalletId,
            order.Currency,
            order.Amount,
            $"Refund for order {order.Id}");

        order.Status = Shared.Models.OrderStatus.Finished;
        await db.SaveChangesAsync();

        return (order, refundTransaction);
    }

    public async Task<SnWalletTransaction> TransferAsync(Guid payerAccountId, Guid payeeAccountId, string currency,
        decimal amount)
    {
        var payerWallet = await wat.GetWalletAsync(payerAccountId);
        if (payerWallet == null)
        {
            throw new InvalidOperationException($"Payer wallet not found for account {payerAccountId}");
        }

        var payeeWallet = await wat.GetWalletAsync(payeeAccountId);
        if (payeeWallet == null)
        {
            throw new InvalidOperationException($"Payee wallet not found for account {payeeAccountId}");
        }

        // Calculate transfer fee (5%)
        decimal fee = Math.Round(amount * 0.05m, 2);

        // Create fee transaction (to system)
        await CreateTransactionAsync(
            payerWallet.Id,
            null,
            currency,
            fee,
            $"Transfer fee for transfer from account {payerAccountId} to {payeeAccountId}",
            Shared.Models.TransactionType.System);

        // Create main transfer transaction
        return await CreateTransactionAsync(
            payerWallet.Id,
            payeeWallet.Id,
            currency,
            amount,
            $"Transfer from account {payerAccountId} to {payeeAccountId}",
            Shared.Models.TransactionType.Transfer);
    }

    public async Task<SnWalletFund> CreateFundAsync(
        Guid creatorAccountId,
        List<Guid> recipientAccountIds,
        string currency,
        decimal totalAmount,
        Shared.Models.FundSplitType splitType,
        string? message = null,
        Duration? expiration = null)
    {
        if (recipientAccountIds.Count == 0)
            throw new ArgumentException("At least one recipient is required");

        if (totalAmount <= 0)
            throw new ArgumentException("Total amount must be positive");

        // Validate all recipient accounts exist and have wallets
        var recipientWallets = new List<SnWallet>();
        foreach (var accountId in recipientAccountIds)
        {
            var wallet = await wat.GetWalletAsync(accountId);
            if (wallet == null)
                throw new InvalidOperationException($"Wallet not found for recipient account {accountId}");
            recipientWallets.Add(wallet);
        }

        // Check creator has sufficient funds
        var creatorWallet = await wat.GetWalletAsync(creatorAccountId);
        if (creatorWallet == null)
            throw new InvalidOperationException($"Creator wallet not found for account {creatorAccountId}");

        var (creatorPocket, _) = await wat.GetOrCreateWalletPocketAsync(creatorWallet.Id, currency);
        if (creatorPocket.Amount < totalAmount)
            throw new InvalidOperationException("Insufficient funds");

        // Calculate amounts for each recipient
        var recipientAmounts = splitType switch
        {
            Shared.Models.FundSplitType.Even => SplitEvenly(totalAmount, recipientAccountIds.Count),
            Shared.Models.FundSplitType.Random => SplitRandomly(totalAmount, recipientAccountIds.Count),
            _ => throw new ArgumentException("Invalid split type")
        };

        var now = SystemClock.Instance.GetCurrentInstant();
        var fund = new SnWalletFund
        {
            CreatorAccountId = creatorAccountId,
            Currency = currency,
            TotalAmount = totalAmount,
            SplitType = splitType,
            Message = message,
            ExpiredAt = now.Plus(expiration ?? Duration.FromHours(24)),
            Recipients = recipientAccountIds.Select((accountId, index) => new SnWalletFundRecipient
            {
                RecipientAccountId = accountId,
                Amount = recipientAmounts[index]
            }).ToList()
        };

        // Deduct from creator's wallet
        await db.WalletPockets
            .Where(p => p.Id == creatorPocket.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Amount, p => p.Amount - totalAmount));

        db.WalletFunds.Add(fund);
        await db.SaveChangesAsync();

        // Load the fund with account data including profiles
        var createdFund = await db.WalletFunds
            .Include(f => f.Recipients)
                .ThenInclude(r => r.RecipientAccount)
                .ThenInclude(a => a.Profile)
            .Include(f => f.CreatorAccount)
                .ThenInclude(a => a.Profile)
            .FirstOrDefaultAsync(f => f.Id == fund.Id);

        return createdFund!;
    }

    private List<decimal> SplitEvenly(decimal totalAmount, int recipientCount)
    {
        var baseAmount = Math.Floor(totalAmount / recipientCount * 100) / 100; // Round down to 2 decimal places
        var remainder = totalAmount - (baseAmount * recipientCount);

        var amounts = new List<decimal>();
        for (int i = 0; i < recipientCount; i++)
        {
            var amount = baseAmount;
            if (i < remainder * 100) // Distribute remainder as 0.01 increments
                amount += 0.01m;
            amounts.Add(amount);
        }

        return amounts;
    }

    private List<decimal> SplitRandomly(decimal totalAmount, int recipientCount)
    {
        var random = new Random();
        var amounts = new List<decimal>();

        // Generate random amounts that sum to total
        decimal remaining = totalAmount;
        for (int i = 0; i < recipientCount - 1; i++)
        {
            // Ensure each recipient gets at least 0.01 and leave enough for remaining recipients
            var maxAmount = remaining - (recipientCount - i - 1) * 0.01m;
            var minAmount = 0.01m;
            var amount = Math.Round((decimal)random.NextDouble() * (maxAmount - minAmount) + minAmount, 2);
            amounts.Add(amount);
            remaining -= amount;
        }

        // Last recipient gets the remainder
        amounts.Add(Math.Round(remaining, 2));

        return amounts;
    }

    public async Task<SnWalletTransaction> ReceiveFundAsync(Guid recipientAccountId, Guid fundId)
    {
        var fund = await db.WalletFunds
            .Include(f => f.Recipients)
            .FirstOrDefaultAsync(f => f.Id == fundId);

        if (fund == null)
            throw new InvalidOperationException("Fund not found");

        if (fund.Status == Shared.Models.FundStatus.Expired || fund.Status == Shared.Models.FundStatus.Refunded)
            throw new InvalidOperationException("Fund is no longer available");

        var recipient = fund.Recipients.FirstOrDefault(r => r.RecipientAccountId == recipientAccountId);
        if (recipient == null)
            throw new InvalidOperationException("You are not a recipient of this fund");

        if (recipient.IsReceived)
            throw new InvalidOperationException("You have already received this fund");

        var recipientWallet = await wat.GetWalletAsync(recipientAccountId);
        if (recipientWallet == null)
            throw new InvalidOperationException("Recipient wallet not found");

        // Create transaction to transfer funds to recipient
        var transaction = await CreateTransactionAsync(
            payerWalletId: null, // System transfer
            payeeWalletId: recipientWallet.Id,
            currency: fund.Currency,
            amount: recipient.Amount,
            remarks: $"Received fund portion from {fund.CreatorAccountId}",
            type: Shared.Models.TransactionType.System,
            silent: true
        );

        // Mark as received
        recipient.IsReceived = true;
        recipient.ReceivedAt = SystemClock.Instance.GetCurrentInstant();

        // Update fund status
        var allReceived = fund.Recipients.All(r => r.IsReceived);
        if (allReceived)
            fund.Status = Shared.Models.FundStatus.FullyReceived;
        else
            fund.Status = Shared.Models.FundStatus.PartiallyReceived;

        await db.SaveChangesAsync();

        return transaction;
    }

    public async Task ProcessExpiredFundsAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var expiredFunds = await db.WalletFunds
            .Include(f => f.Recipients)
            .Where(f => f.Status == Shared.Models.FundStatus.Created || f.Status == Shared.Models.FundStatus.PartiallyReceived)
            .Where(f => f.ExpiredAt < now)
            .ToListAsync();

        foreach (var fund in expiredFunds)
        {
            // Calculate unclaimed amount
            var unclaimedAmount = fund.Recipients
                .Where(r => !r.IsReceived)
                .Sum(r => r.Amount);

            if (unclaimedAmount > 0)
            {
                // Refund to creator
                var creatorWallet = await wat.GetWalletAsync(fund.CreatorAccountId);
                if (creatorWallet != null)
                {
                    await CreateTransactionAsync(
                        payerWalletId: null, // System refund
                        payeeWalletId: creatorWallet.Id,
                        currency: fund.Currency,
                        amount: unclaimedAmount,
                        remarks: $"Refund for expired fund {fund.Id}",
                        type: Shared.Models.TransactionType.System,
                        silent: true
                    );
                }
            }

            fund.Status = Shared.Models.FundStatus.Expired;
        }

        await db.SaveChangesAsync();
    }

    public async Task<WalletOverview> GetWalletOverviewAsync(Guid accountId, DateTime? startDate = null, DateTime? endDate = null)
    {
        var wallet = await wat.GetWalletAsync(accountId);
        if (wallet == null)
            throw new InvalidOperationException("Wallet not found");

        var query = db.PaymentTransactions
            .Where(t => t.PayerWalletId == wallet.Id || t.PayeeWalletId == wallet.Id);

        if (startDate.HasValue)
            query = query.Where(t => t.CreatedAt >= Instant.FromDateTimeUtc(startDate.Value.ToUniversalTime()));
        if (endDate.HasValue)
            query = query.Where(t => t.CreatedAt <= Instant.FromDateTimeUtc(endDate.Value.ToUniversalTime()));

        var transactions = await query.ToListAsync();

        var overview = new WalletOverview
        {
            AccountId = accountId,
            StartDate = startDate?.ToString("O"),
            EndDate = endDate?.ToString("O"),
            Summary = new Dictionary<string, TransactionSummary>()
        };

        // Group transactions by type and currency
        var groupedTransactions = transactions
            .GroupBy(t => new { t.Type, t.Currency })
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var group in groupedTransactions)
        {
            var typeName = group.Key.Type.ToString();
            var currency = group.Key.Currency;

            if (!overview.Summary.ContainsKey(typeName))
            {
                overview.Summary[typeName] = new TransactionSummary
                {
                    Type = typeName,
                    Currencies = new Dictionary<string, CurrencySummary>()
                };
            }

            var currencySummary = new CurrencySummary
            {
                Currency = currency,
                Income = 0,
                Spending = 0,
                Net = 0
            };

            foreach (var transaction in group.Value)
            {
                if (transaction.PayeeWalletId == wallet.Id)
                {
                    // Money coming in
                    currencySummary.Income += transaction.Amount;
                }
                else if (transaction.PayerWalletId == wallet.Id)
                {
                    // Money going out
                    currencySummary.Spending += transaction.Amount;
                }
            }

            currencySummary.Net = currencySummary.Income - currencySummary.Spending;
            overview.Summary[typeName].Currencies[currency] = currencySummary;
        }

        // Calculate totals
        overview.TotalIncome = overview.Summary.Values.Sum(s => s.Currencies.Values.Sum(c => c.Income));
        overview.TotalSpending = overview.Summary.Values.Sum(s => s.Currencies.Values.Sum(c => c.Spending));
        overview.NetTotal = overview.TotalIncome - overview.TotalSpending;

        return overview;
    }
}

public class WalletOverview
{
    public Guid AccountId { get; set; }
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public Dictionary<string, TransactionSummary> Summary { get; set; } = new();
    public decimal TotalIncome { get; set; }
    public decimal TotalSpending { get; set; }
    public decimal NetTotal { get; set; }
}

public class TransactionSummary
{
    public string Type { get; set; } = null!;
    public Dictionary<string, CurrencySummary> Currencies { get; set; } = new();
}

public class CurrencySummary
{
    public string Currency { get; set; } = null!;
    public decimal Income { get; set; }
    public decimal Spending { get; set; }
    public decimal Net { get; set; }
}
