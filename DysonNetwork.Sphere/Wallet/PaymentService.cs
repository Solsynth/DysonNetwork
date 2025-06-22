using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NodaTime;

namespace DysonNetwork.Sphere.Wallet;

public class PaymentService(AppDatabase db, WalletService wat)
{
    public async Task<Order> CreateOrderAsync(
        Guid? payeeWalletId,
        string currency,
        decimal amount,
        Duration? expiration = null,
        string? appIdentifier = null,
        Dictionary<string, object>? meta = null,
        bool reuseable = true
    )
    {
        // Check if there's an existing unpaid order that can be reused
        if (reuseable && appIdentifier != null)
        {
            var existingOrder = await db.PaymentOrders
                .Where(o => o.Status == OrderStatus.Unpaid &&
                       o.PayeeWalletId == payeeWalletId &&
                       o.Currency == currency &&
                       o.Amount == amount &&
                       o.AppIdentifier == appIdentifier &&
                       o.ExpiredAt > SystemClock.Instance.GetCurrentInstant())
                .FirstOrDefaultAsync();

            // If an existing order is found, check if meta matches
            if (existingOrder != null && meta != null && existingOrder.Meta != null)
            {
                // Compare meta dictionaries - if they are equivalent, reuse the order
                var metaMatches = existingOrder.Meta.Count == meta.Count &&
                                  !existingOrder.Meta.Except(meta).Any();
                               
                if (metaMatches)
                {
                    return existingOrder;
                }
            }
        }

        // Create a new order if no reusable order was found
        var order = new Order
        {
            PayeeWalletId = payeeWalletId,
            Currency = currency,
            Amount = amount,
            ExpiredAt = SystemClock.Instance.GetCurrentInstant().Plus(expiration ?? Duration.FromHours(24)),
            AppIdentifier = appIdentifier,
            Meta = meta
        };

        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync();
        return order;
    }

    public async Task<Transaction> CreateTransactionWithAccountAsync(
        Guid? payerAccountId,
        Guid? payeeAccountId,
        string currency,
        decimal amount,
        string? remarks = null,
        TransactionType type = TransactionType.System
    )
    {
        Wallet? payer = null, payee = null;
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

    public async Task<Transaction> CreateTransactionAsync(
        Guid? payerWalletId,
        Guid? payeeWalletId,
        string currency,
        decimal amount,
        string? remarks = null,
        TransactionType type = TransactionType.System
    )
    {
        if (payerWalletId == null && payeeWalletId == null)
            throw new ArgumentException("At least one wallet must be specified.");
        if (amount <= 0) throw new ArgumentException("Cannot create transaction with negative or zero amount.");

        var transaction = new Transaction
        {
            PayerWalletId = payerWalletId,
            PayeeWalletId = payeeWalletId,
            Currency = currency,
            Amount = amount,
            Remarks = remarks,
            Type = type
        };

        if (payerWalletId.HasValue)
        {
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
        return transaction;
    }

    public async Task<Order> PayOrderAsync(Guid orderId, Guid payerWalletId)
    {
        var order = await db.PaymentOrders
            .Include(o => o.Transaction)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            throw new InvalidOperationException("Order not found");
        }

        if (order.Status != OrderStatus.Unpaid)
        {
            throw new InvalidOperationException($"Order is in invalid status: {order.Status}");
        }

        if (order.ExpiredAt < SystemClock.Instance.GetCurrentInstant())
        {
            order.Status = OrderStatus.Expired;
            await db.SaveChangesAsync();
            throw new InvalidOperationException("Order has expired");
        }

        var transaction = await CreateTransactionAsync(
            payerWalletId,
            order.PayeeWalletId,
            order.Currency,
            order.Amount,
            order.Remarks ?? $"Payment for Order #{order.Id}",
            type: TransactionType.Order);

        order.TransactionId = transaction.Id;
        order.Transaction = transaction;
        order.Status = OrderStatus.Paid;

        await db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> CancelOrderAsync(Guid orderId)
    {
        var order = await db.PaymentOrders.FindAsync(orderId);
        if (order == null)
        {
            throw new InvalidOperationException("Order not found");
        }

        if (order.Status != OrderStatus.Unpaid)
        {
            throw new InvalidOperationException($"Cannot cancel order in status: {order.Status}");
        }

        order.Status = OrderStatus.Cancelled;
        await db.SaveChangesAsync();
        return order;
    }

    public async Task<(Order Order, Transaction RefundTransaction)> RefundOrderAsync(Guid orderId)
    {
        var order = await db.PaymentOrders
            .Include(o => o.Transaction)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            throw new InvalidOperationException("Order not found");
        }

        if (order.Status != OrderStatus.Paid)
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

        order.Status = OrderStatus.Finished;
        await db.SaveChangesAsync();

        return (order, refundTransaction);
    }

    public async Task<Transaction> TransferAsync(Guid payerAccountId, Guid payeeAccountId, string currency,
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
            TransactionType.Transfer);
    }
}