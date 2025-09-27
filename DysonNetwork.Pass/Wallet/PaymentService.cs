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

        return await CreateTransactionAsync(
            payerWallet.Id,
            payeeWallet.Id,
            currency,
            amount,
            $"Transfer from account {payerAccountId} to {payeeAccountId}",
            Shared.Models.TransactionType.Transfer);
    }
}