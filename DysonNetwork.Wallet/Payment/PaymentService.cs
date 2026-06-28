using System.Data;
using System.Globalization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Wallet.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Localization;
using NATS.Client.Core;
using NATS.Net;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

public class PaymentService(
    AppDatabase db,
    WalletService wat,
    DyRingService.DyRingServiceClient pusher,
    ILocalizationService localizer,
    Shared.EventBus.IEventBus eventBus,
    RemoteAccountService remoteAccounts,
    RemoteRingService ring,
    ILogger<PaymentService> logger
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
        bool reuseable = true,
        List<SnWalletOrderItem>? items = null
    )
    {
        // ponytail: skip reuse check when items are present — line-item orders are never reused
        var now = SystemClock.Instance.GetCurrentInstant();
        if (items is not { Count: > 0 } && reuseable && appIdentifier != null)
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

            if (existingOrder != null && meta != null && existingOrder.Meta != null)
            {
                var metaMatches = existingOrder.Meta.Count == meta.Count &&
                                  !existingOrder.Meta.Except(meta).Any();
                if (metaMatches)
                    return existingOrder;
            }
        }

        var order = new SnWalletOrder
        {
            PayeeWalletId = payeeWalletId,
            Currency = currency,
            Amount = TruncateToThreeDecimals(amount),
            ExpiredAt = now.Plus(expiration ?? Duration.FromHours(24)),
            AppIdentifier = appIdentifier,
            ProductIdentifier = productIdentifier,
            Remarks = remarks,
            Meta = meta
        };

        db.PaymentOrders.Add(order);

        if (items is { Count: > 0 })
        {
            foreach (var item in items)
            {
                item.OrderId = order.Id;
                item.Order = order;
                db.WalletOrderItems.Add(item);
            }
        }

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

    public async Task<SnWalletTransferRequest> CreateTransferRequestAsync(
        Guid creatorAccountId,
        Guid payeeWalletId,
        string currency,
        decimal amount,
        string? remark = null,
        Duration? expiration = null,
        bool freeze = false,
        bool requireConfirmation = false
    )
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be greater than zero.");

        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == payeeWalletId)
            ?? throw new InvalidOperationException("Payee wallet not found.");
        if (string.IsNullOrWhiteSpace(wallet.PublicId))
            throw new InvalidOperationException("Payee wallet must have a public ID enabled.");

        var now = SystemClock.Instance.GetCurrentInstant();
        var request = new SnWalletTransferRequest
        {
            CreatorAccountId = creatorAccountId,
            PayeeWalletId = payeeWalletId,
            Currency = currency,
            Amount = TruncateToThreeDecimals(amount),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
            Freeze = freeze,
            RequireConfirmation = requireConfirmation,
            ExpiresAt = now.Plus(expiration ?? Duration.FromHours(24)),
        };

        db.WalletTransferRequests.Add(request);
        await db.SaveChangesAsync();
        return request;
    }

    public async Task<SnWalletTransferRequest?> GetTransferRequestAsync(Guid requestId)
    {
        var request = await db.WalletTransferRequests
            .Include(r => r.PayeeWallet)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request == null) return null;

        var now = SystemClock.Instance.GetCurrentInstant();
        if (request.Status == WalletTransferRequestStatus.Active && request.ExpiresAt < now)
        {
            request.Status = WalletTransferRequestStatus.Expired;
            await db.SaveChangesAsync();
        }

        return request;
    }

    public async Task<SnWalletTransferRequest> FulfillTransferRequestAsync(
        Guid requestId,
        Guid transactionId
    )
    {
        var request = await db.WalletTransferRequests.FirstOrDefaultAsync(r => r.Id == requestId)
            ?? throw new InvalidOperationException("Transfer request not found.");
        if (request.Status != WalletTransferRequestStatus.Active)
            throw new InvalidOperationException("Transfer request is no longer active.");
        if (request.ExpiresAt < SystemClock.Instance.GetCurrentInstant())
            throw new InvalidOperationException("Transfer request has expired.");

        request.Status = WalletTransferRequestStatus.Fulfilled;
        request.TransactionId = transactionId;
        request.FulfilledAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        return request;
    }

    public async Task<SnWalletTransaction> CreateTransactionAsync(
        Guid? payerWalletId,
        Guid? payeeWalletId,
        string currency,
        decimal amount,
        string? remarks = null,
        Shared.Models.TransactionType type = Shared.Models.TransactionType.System,
        bool silent = false,
        bool autoSave = true,
        bool force = false,
        bool freeze = false,
        bool requireConfirmation = false
    )
    {
        if (payerWalletId == null && payeeWalletId == null)
            throw new ArgumentException("At least one wallet must be specified.");
        if (amount <= 0) throw new ArgumentException("Cannot create transaction with negative or zero amount.");

        var truncatedAmount = TruncateToThreeDecimals(amount);
        var now = SystemClock.Instance.GetCurrentInstant();

        // Determine transaction status based on flags
        Shared.Models.TransactionStatus status;
        Instant? expiresAt = null;
        Instant? frozenAt = null;

        if (!freeze && !requireConfirmation)
        {
            // Instant transfer
            status = Shared.Models.TransactionStatus.Confirmed;
        }
        else if (freeze && !requireConfirmation)
        {
            // Frozen: hold for 24hr then auto-release
            status = Shared.Models.TransactionStatus.Frozen;
            frozenAt = now;
            expiresAt = now.Plus(Duration.FromHours(24));
        }
        else if (!freeze && requireConfirmation)
        {
            // Confirm-required: wait for payee, auto-refund after 24hr
            status = Shared.Models.TransactionStatus.Pending;
            expiresAt = now.Plus(Duration.FromHours(24));
        }
        else
        {
            // Both frozen + confirm-required: freeze first, then payee must confirm
            status = Shared.Models.TransactionStatus.Frozen;
            frozenAt = now;
            expiresAt = now.Plus(Duration.FromHours(24));
        }

        var transaction = new SnWalletTransaction
        {
            PayerWalletId = payerWalletId,
            PayeeWalletId = payeeWalletId,
            Currency = currency,
            Amount = truncatedAmount,
            Remarks = remarks,
            Type = type,
            Status = status,
            IsFrozen = freeze,
            RequireConfirmation = requireConfirmation,
            FrozenAt = frozenAt,
            ExpiresAt = expiresAt,
            ConfirmedAt = status == Shared.Models.TransactionStatus.Confirmed ? now : null
        };

        SnWallet? payerWallet = null, payeeWallet = null;

        if (payerWalletId.HasValue)
        {
            payerWallet = await db.Wallets.FirstOrDefaultAsync(e => e.Id == payerWalletId.Value);

            var (payerPocket, isNewlyCreated) =
                await wat.GetOrCreateWalletPocketAsync(payerWalletId.Value, currency);

            if (!force)
                if (isNewlyCreated || payerPocket.Amount < truncatedAmount)
                    throw new InvalidOperationException("Insufficient funds");

            // Always deduct from payer pocket
            await db.WalletPockets
                .Where(p => p.Id == payerPocket.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(p => p.Amount, p => p.Amount - truncatedAmount));

            // If pending/frozen, track held amount
            if (status is Shared.Models.TransactionStatus.Pending or Shared.Models.TransactionStatus.Frozen)
            {
                await db.WalletPockets
                    .Where(p => p.Id == payerPocket.Id)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.HeldAmount, p => p.HeldAmount + truncatedAmount));
            }
        }

        // Only credit payee immediately for instant (confirmed) transactions
        if (payeeWalletId.HasValue && status == Shared.Models.TransactionStatus.Confirmed)
        {
            payeeWallet = await db.Wallets.FirstOrDefaultAsync(e => e.Id == payeeWalletId.Value);

            var (payeePocket, isNewlyCreated) =
                await wat.GetOrCreateWalletPocketAsync(payeeWalletId.Value, currency, truncatedAmount);

            if (!isNewlyCreated)
                await db.WalletPockets
                    .Where(p => p.Id == payeePocket.Id)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.Amount, p => p.Amount + truncatedAmount));
        }
        else if (payeeWalletId.HasValue)
        {
            payeeWallet = await db.Wallets.FirstOrDefaultAsync(e => e.Id == payeeWalletId.Value);
        }

        db.PaymentTransactions.Add(transaction);
        if (autoSave)
            await db.SaveChangesAsync();

        if (!silent)
        {
            await NotifyNewTransaction(transaction, payerWallet, payeeWallet);

            // Send real-time WebSocket updates
            await NotifyTransactionStatusChange(transaction,
                Shared.Models.WebSocketPacketType.WalletTransactionCreated,
                payerWallet?.AccountId, payeeWallet?.AccountId);
        }

        // Notify pocket balance updates via WebSocket
        if (payerWalletId.HasValue)
            await NotifyPocketUpdated(payerWalletId.Value, currency, 0, 0);
        if (payeeWalletId.HasValue && transaction.Status == Shared.Models.TransactionStatus.Confirmed)
            await NotifyPocketUpdated(payeeWalletId.Value, currency, 0, 0);

        return transaction;
    }

    private async Task NotifyNewTransaction(SnWalletTransaction transaction, SnWallet? payerWallet,
        SnWallet? payeeWallet)
    {
        if (payerWallet?.AccountId is Guid payerAccountId)
        {
            var accountInfo = await remoteAccounts.GetAccount(payerAccountId);
            var locale = accountInfo.Language;

            // Due to ID is uuid, it longer than 8 words for sure
            var readableTransactionId = transaction.Id.ToString().Replace("-", "")[..8];
            var readableTransactionRemark = transaction.Remarks ?? $"#{readableTransactionId}";

            await pusher.SendPushNotificationToUserAsync(
                new DySendPushNotificationToUserRequest
                {
                    UserId = payerAccountId.ToString(),
                    Notification = new DyPushNotification
                    {
                        Topic = "wallets.transactions",
                        Title = localizer.Get("transactionNewTitle", locale: locale),
                        Subtitle = readableTransactionRemark,
                        Body = transaction.Amount > 0
                            ? localizer.Get("transactionNewBodyMinus", locale: locale, args: new
                            {
                                amount = transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                currency = GetTranslationForCurrency(transaction.Currency, locale)
                            })
                            : localizer.Get("transactionNewBodyPlus", locale: locale, args: new
                            {
                                amount = transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                currency = GetTranslationForCurrency(transaction.Currency, locale)
                            }),
                        IsSavable = true
                    }
                }
            );
        }

        if (payeeWallet?.AccountId is Guid payeeAccountId)
        {
            var accountInfo = await remoteAccounts.GetAccount(payeeAccountId);
            var locale = accountInfo.Language;

            // Due to ID is uuid, it longer than 8 words for sure
            var readableTransactionId = transaction.Id.ToString().Replace("-", "")[..8];
            var readableTransactionRemark = transaction.Remarks ?? $"#{readableTransactionId}";

            await pusher.SendPushNotificationToUserAsync(
                new DySendPushNotificationToUserRequest
                {
                    UserId = payeeAccountId.ToString(),
                    Notification = new DyPushNotification
                    {
                        Topic = "wallets.transactions",
                        Title = localizer.Get("transactionNewTitle", locale: locale),
                        Subtitle = readableTransactionRemark,
                        Body = transaction.Amount > 0
                            ? localizer.Get("transactionNewBodyPlus", locale: locale, args: new
                            {
                                amount = transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                currency = GetTranslationForCurrency(transaction.Currency, locale)
                            })
                            : localizer.Get("transactionNewBodyMinus", locale: locale, args: new
                            {
                                amount = transaction.Amount.ToString(CultureInfo.InvariantCulture),
                                currency = GetTranslationForCurrency(transaction.Currency, locale)
                            }),
                        IsSavable = true
                    }
                }
            );
        }
    }

    #region Wallet WebSocket Real-Time Updates

    private async Task SendWalletWebSocketPacket(Guid accountId, string type, object data)
    {
        try
        {
            var jsonBytes = InfraObjectCoder.ConvertObjectToByteString(data).ToByteArray();
            await ring.SendWebSocketPacketToUser(accountId.ToString(), type, jsonBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send wallet WebSocket packet {Type} to user {AccountId}", type, accountId);
        }
    }

    private async Task NotifyTransactionStatusChange(SnWalletTransaction transaction, string packetType,
        Guid? payerAccountId, Guid? payeeAccountId)
    {
        var payload = new
        {
            id = transaction.Id.ToString(),
            type = transaction.Type,
            amount = transaction.Amount,
            currency = transaction.Currency,
            status = transaction.Status,
            remarks = transaction.Remarks,
            payer_wallet_id = transaction.PayerWalletId?.ToString(),
            payee_wallet_id = transaction.PayeeWalletId?.ToString(),
            is_frozen = transaction.IsFrozen,
            require_confirmation = transaction.RequireConfirmation,
            frozen_at = transaction.FrozenAt,
            expires_at = transaction.ExpiresAt,
            confirmed_at = transaction.ConfirmedAt
        };

        var tasks = new List<Task>();

        if (payerAccountId.HasValue)
            tasks.Add(SendWalletWebSocketPacket(payerAccountId.Value, packetType, payload));

        if (payeeAccountId.HasValue)
            tasks.Add(SendWalletWebSocketPacket(payeeAccountId.Value, packetType, payload));

        await Task.WhenAll(tasks);
    }

    private async Task NotifyPocketUpdated(Guid walletId, string currency, decimal amount, decimal heldAmount)
    {
        var pocket = await db.WalletPockets
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.WalletId == walletId && p.Currency == currency);

        if (pocket == null) return;

        var ownerAccountId = await db.Wallets
            .Where(w => w.Id == walletId)
            .Select(w => w.AccountId)
            .FirstOrDefaultAsync();

        if (!ownerAccountId.HasValue) return;

        var payload = new
        {
            wallet_id = walletId.ToString(),
            currency = pocket.Currency,
            amount = pocket.Amount,
            held_amount = pocket.HeldAmount,
            available_amount = pocket.Amount - pocket.HeldAmount
        };

        await SendWalletWebSocketPacket(ownerAccountId.Value,
            Shared.Models.WebSocketPacketType.WalletPocketUpdated, payload);
    }

    #endregion

    public async Task<SnWalletOrder> PayOrderAsync(Guid orderId, SnWallet payerWallet)
    {
        var order = await db.PaymentOrders
            .Include(o => o.Transaction)
            .Include(o => o.PayeeWallet)
            .FirstOrDefaultAsync(o => o.Id == orderId) ?? throw new InvalidOperationException("Order not found");

        if (order.Status == Shared.Models.OrderStatus.Paid)
        {
            if (!payerWallet.AccountId.HasValue)
                throw new InvalidOperationException("Payer wallet is not account-owned.");

            await eventBus.PublishAsync(PaymentOrderEventBase.Type, new PaymentOrderEvent
            {
                OrderId = order.Id,
                WalletId = payerWallet.Id,
                AccountId = payerWallet.AccountId.Value,
                AppIdentifier = order.AppIdentifier,
                ProductIdentifier = order.ProductIdentifier,
                Meta = order.Meta ?? [],
                Status = (int)order.Status,
            });

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

        if (!payerWallet.AccountId.HasValue)
            throw new InvalidOperationException("Payer wallet is not account-owned.");

        await eventBus.PublishAsync(PaymentOrderEventBase.Type, new PaymentOrderEvent
        {
            OrderId = order.Id,
            WalletId = payerWallet.Id,
            AccountId = payerWallet.AccountId.Value,
            AppIdentifier = order.AppIdentifier,
            ProductIdentifier = order.ProductIdentifier,
            Meta = order.Meta ?? [],
            Status = (int)order.Status,
        });

        return order;
    }

    private async Task NotifyOrderPaid(SnWalletOrder order, SnWallet? payerWallet, SnWallet? payeeWallet)
    {
        if (payerWallet?.AccountId is Guid payerAccountId)
        {
            var accountInfo = await remoteAccounts.GetAccount(payerAccountId);
            var locale = accountInfo.Language;

            // Due to ID is uuid, it longer than 8 words for sure
            var readableOrderId = order.Id.ToString().Replace("-", "")[..8];
            var readableOrderRemark = order.Remarks ?? $"#{readableOrderId}";

            await pusher.SendPushNotificationToUserAsync(
                new DySendPushNotificationToUserRequest
                {
                    UserId = payerAccountId.ToString(),
                    Notification = new DyPushNotification
                    {
                        Topic = "wallets.orders.paid",
                        Title = localizer.Get("orderPaidTitle", args: new { orderId = $"#{readableOrderId}" }),
                        Subtitle = readableOrderRemark,
                        Body = localizer.Get("orderPaidBody", args: new
                        {
                            amount = order.Amount.ToString(CultureInfo.InvariantCulture),
                            currency = GetTranslationForCurrency(order.Currency, locale)
                        }),
                        IsSavable = true
                    }
                }
            );
        }

        if (payeeWallet?.AccountId is Guid payeeAccountId)
        {
            var accountInfo = await remoteAccounts.GetAccount(payeeAccountId);
            var locale = accountInfo.Language;

            // Due to ID is uuid, it longer than 8 words for sure
            var readableOrderId = order.Id.ToString().Replace("-", "")[..8];
            var readableOrderRemark = order.Remarks ?? $"#{readableOrderId}";

            await pusher.SendPushNotificationToUserAsync(
                new DySendPushNotificationToUserRequest
                {
                    UserId = payeeAccountId.ToString(),
                    Notification = new DyPushNotification
                    {
                        Topic = "wallets.orders.paid",
                        Title = localizer.Get("orderPaidTitle", locale: locale,
                            args: new { orderId = $"#{readableOrderId}" }),
                        Subtitle = readableOrderRemark,
                        Body = localizer.Get("orderPaidBody", locale: locale, args: new
                        {
                            amount = order.Amount.ToString(CultureInfo.InvariantCulture),
                            currency = GetTranslationForCurrency(order.Currency, locale),
                        }),
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
        decimal amount, string? remarks = null)
    {
        var payerWallet = await wat.GetAccountWalletAsync(payerAccountId);
        if (payerWallet == null)
        {
            throw new InvalidOperationException($"Payer wallet not found for account {payerAccountId}");
        }

        var payeeWallet = await wat.GetAccountWalletAsync(payeeAccountId);
        if (payeeWallet == null)
        {
            throw new InvalidOperationException($"Payee wallet not found for account {payeeAccountId}");
        }

        // Calculate transfer fee (5%)
        var truncatedAmount = TruncateToThreeDecimals(amount);
        decimal fee = TruncateToThreeDecimals(truncatedAmount * 0.05m);
        decimal finalCost = truncatedAmount + fee;

        // Make sure the account has sufficient balanace for both fee and the transfer
        var (payerPocket, isNewlyCreated) =
            await wat.GetOrCreateWalletPocketAsync(payerWallet.Id, currency, amount);

        if (isNewlyCreated || payerPocket.Amount < finalCost)
            throw new InvalidOperationException("Insufficient funds");

        var payeeAccount = await remoteAccounts.GetAccount(payeeAccountId);
        var payerAccount = await remoteAccounts.GetAccount(payerAccountId);

        // Create main transfer transaction
        var transferRemarks = remarks ?? localizer.Get("transferRemark", payeeAccount.Language,
            new { payer = $"@{payerAccount.Name}", payee = $"@{payeeAccount.Name}" });

        var transaction = await CreateTransactionAsync(
            payerWallet.Id,
            payeeWallet.Id,
            currency,
            truncatedAmount,
            transferRemarks,
            Shared.Models.TransactionType.Transfer
        );

        // Create fee transaction (to system)
        await CreateTransactionAsync(
            payerWallet.Id,
            null,
            currency,
            fee,
            localizer.Get("transferFeeRemark", payerAccount.Language, new { id = transaction.Id.ToString()[..8] })
        );

        return transaction;
    }

    public async Task<SnWalletTransaction> TransferBetweenWalletsAsync(Guid payerWalletId, Guid payeeWalletId,
        string currency, decimal amount, string? remarks = null, bool freeze = false, bool requireConfirmation = false)
    {
        var payerWallet = await wat.GetWalletAsync(payerWalletId);
        if (payerWallet == null)
            throw new InvalidOperationException($"Payer wallet not found: {payerWalletId}");

        var payeeWallet = await wat.GetWalletAsync(payeeWalletId);
        if (payeeWallet == null)
            throw new InvalidOperationException($"Payee wallet not found: {payeeWalletId}");

        var truncatedAmount = TruncateToThreeDecimals(amount);
        decimal fee = TruncateToThreeDecimals(truncatedAmount * 0.05m);
        decimal finalCost = truncatedAmount + fee;

        var (payerPocket, isNewlyCreated) =
            await wat.GetOrCreateWalletPocketAsync(payerWallet.Id, currency, amount);

        if (isNewlyCreated || payerPocket.Amount < finalCost)
            throw new InvalidOperationException("Insufficient funds");

        var transaction = await CreateTransactionAsync(
            payerWallet.Id,
            payeeWallet.Id,
            currency,
            truncatedAmount,
            remarks,
            Shared.Models.TransactionType.Transfer,
            freeze: freeze,
            requireConfirmation: requireConfirmation
        );

        // Fee is always instant (not frozen/confirm-required)
        await CreateTransactionAsync(
            payerWallet.Id,
            null,
            currency,
            fee,
            localizer.Get("transferFeeRemark", args: new { id = transaction.Id.ToString()[..8] })
        );

        return transaction;
    }

    public async Task<SnWalletFund> CreateFundAsync(
        Guid creatorAccountId,
        List<Guid> recipientAccountIds,
        string currency,
        decimal totalAmount,
        int amountOfSplits,
        Shared.Models.FundSplitType splitType,
        string? message = null,
        Duration? expiration = null)
    {
        if (totalAmount <= 0)
            throw new ArgumentException("Total amount must be positive");

        // Check creator has sufficient funds
        var creatorWallet = await wat.GetAccountWalletAsync(creatorAccountId);
        if (creatorWallet == null)
            throw new InvalidOperationException($"Creator wallet not found for account {creatorAccountId}");

        // Validate all recipient accounts exist and have wallets
        foreach (var accountId in recipientAccountIds)
        {
            var wallet = await wat.GetAccountWalletAsync(accountId);
            if (wallet == null)
                throw new InvalidOperationException($"Wallet not found for recipient account {accountId}");
        }

        var truncatedTotalAmount = TruncateToThreeDecimals(totalAmount);
        var (creatorPocket, _) = await wat.GetOrCreateWalletPocketAsync(creatorWallet.Id, currency);
        if (creatorPocket.Amount < truncatedTotalAmount)
            throw new InvalidOperationException("Insufficient funds");

        var now = SystemClock.Instance.GetCurrentInstant();
        var fund = new SnWalletFund
        {
            CreatorAccountId = creatorAccountId,
            Currency = currency,
            TotalAmount = truncatedTotalAmount,
            RemainingAmount = truncatedTotalAmount,
            AmountOfSplits = amountOfSplits,
            SplitType = splitType,
            Message = message,
            ExpiredAt = now.Plus(expiration ?? Duration.FromHours(24)),
            IsOpen = recipientAccountIds.Count == 0,
            Recipients = recipientAccountIds.Select(accountId => new SnWalletFundRecipient
            {
                RecipientAccountId = accountId,
                Amount = 0 // Amount will be calculated dynamically when claimed
            }).ToList()
        };

        // Deduct from creator's wallet
        await db.WalletPockets
            .Where(p => p.Id == creatorPocket.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Amount, p => p.Amount - truncatedTotalAmount));

        db.WalletFunds.Add(fund);
        await db.SaveChangesAsync();

        // Load the fund with account data including profiles
        var createdFund = await db.WalletFunds
            .Include(f => f.Recipients)
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
        var remaining = totalAmount;
        for (var i = 0; i < recipientCount - 1; i++)
        {
            // Ensure each recipient gets at least 50% of the split evenly amount
            var evenSplit = remaining / recipientCount;
            var minAmount = evenSplit * 0.5m;
            var maxAmount = remaining - (recipientCount - i - 1) * minAmount;
            var amount = Math.Round((decimal)random.NextDouble() * (maxAmount - minAmount) + minAmount, 2);
            amounts.Add(amount);
            remaining -= amount;
        }

        // Last recipient gets the remainder
        amounts.Add(Math.Round(remaining, 2));

        return amounts;
    }

    private decimal CalculateDynamicAmount(SnWalletFund fund)
    {
        if (fund.RemainingAmount <= 0)
            return 0;

        // For open mode funds: use split type calculation
        if (fund.IsOpen)
        {
            var remainingRecipients = fund.AmountOfSplits - fund.Recipients.Count(r => r.IsReceived);
            if (remainingRecipients == 0)
                return 0;

            var amount = fund.SplitType switch
            {
                Shared.Models.FundSplitType.Even => SplitEvenly(fund.RemainingAmount, remainingRecipients)[0],
                Shared.Models.FundSplitType.Random => SplitRandomly(fund.RemainingAmount, remainingRecipients)[0],
                _ => throw new ArgumentException("Invalid split type")
            };
            return Math.Max(amount, 0.01m);
        }

        // For closed mode funds: use split type calculation

        var unclaimedRecipients = fund.Recipients.Count(r => !r.IsReceived);
        if (unclaimedRecipients == 0)
            return 0;

        return fund.SplitType switch
        {
            Shared.Models.FundSplitType.Even => SplitEvenly(fund.RemainingAmount, unclaimedRecipients)[0],
            Shared.Models.FundSplitType.Random => SplitRandomly(fund.RemainingAmount, unclaimedRecipients)[0],
            _ => throw new ArgumentException("Invalid split type")
        };
    }

    public async Task<SnWalletTransaction> ReceiveFundAsync(Guid recipientAccountId, Guid fundId)
    {
        // Use a transaction to ensure atomicity
        await using var transactionScope = await db.Database.BeginTransactionAsync();

        try
        {
            // Load fund with proper locking to prevent concurrent modifications
            var fund = await db.WalletFunds
                .Include(f => f.Recipients)
                .FirstOrDefaultAsync(f => f.Id == fundId);

            if (fund == null)
                throw new InvalidOperationException("Fund not found");

            if (fund.Status is Shared.Models.FundStatus.Expired or Shared.Models.FundStatus.Refunded)
                throw new InvalidOperationException("Fund is no longer available");

            var recipient = fund.Recipients.FirstOrDefault(r => r.RecipientAccountId == recipientAccountId);

            switch (recipient)
            {
                // Handle open mode fund - create recipient if not exists
                case null when fund.IsOpen:
                {
                    // Check if recipient has already claimed from this fund
                    var existingClaim = fund.Recipients.FirstOrDefault(r => r.RecipientAccountId == recipientAccountId);
                    if (existingClaim != null)
                        throw new InvalidOperationException("You have already claimed from this fund");

                    // Calculate amount for new recipient
                    var amount = CalculateDynamicAmount(fund);
                    if (amount <= 0)
                        throw new InvalidOperationException("No funds remaining to claim");

                    var truncatedAmount = TruncateToThreeDecimals(amount);

                    // Create new recipient
                    recipient = new SnWalletFundRecipient
                    {
                        RecipientAccountId = recipientAccountId,
                        Amount = truncatedAmount,
                        IsReceived = false,
                        FundId = fund.Id,
                    };
                    db.WalletFundRecipients.Add(recipient);
                    await db.SaveChangesAsync();

                    fund.RemainingAmount -= truncatedAmount;
                    break;
                }
                case null:
                    throw new InvalidOperationException("You are not a recipient of this fund");
            }

            // For closed mode funds, calculate amount dynamically if not already set
            if (!fund.IsOpen && recipient.Amount == 0)
            {
                var amount = CalculateDynamicAmount(fund);
                if (amount <= 0)
                    throw new InvalidOperationException("No funds remaining to claim");

                var truncatedAmount = TruncateToThreeDecimals(amount);
                recipient.Amount = truncatedAmount;
                fund.RemainingAmount -= truncatedAmount;
            }

            db.Update(fund);
            await db.SaveChangesAsync();

            if (recipient.IsReceived)
                throw new InvalidOperationException("You have already received this fund");

            var recipientWallet = await wat.GetAccountWalletAsync(recipientAccountId);
            if (recipientWallet == null)
                throw new InvalidOperationException("Recipient wallet not found");

            // Create transaction to transfer funds to recipient
            // This call will save changes once
            var walletTransaction = await CreateTransactionAsync(
                payerWalletId: null, // System transfer
                payeeWalletId: recipientWallet.Id,
                currency: fund.Currency,
                amount: recipient.Amount,
                remarks: $"Received fund portion from {fund.CreatorAccountId}",
                type: Shared.Models.TransactionType.System
            );

            // Mark as received
            recipient.IsReceived = true;
            recipient.ReceivedAt = SystemClock.Instance.GetCurrentInstant();

            // Update fund status
            if (fund.IsOpen)
            {
                fund.Status = fund.RemainingAmount <= 0
                    ? Shared.Models.FundStatus.FullyReceived
                    : Shared.Models.FundStatus.PartiallyReceived;
            }
            else
            {
                var allReceived = fund.Recipients.All(r => r.IsReceived);
                fund.Status = allReceived
                    ? Shared.Models.FundStatus.FullyReceived
                    : Shared.Models.FundStatus.PartiallyReceived;
            }

            db.Update(fund);
            await db.SaveChangesAsync();
            await transactionScope.CommitAsync();

            return walletTransaction;
        }
        catch
        {
            await transactionScope.RollbackAsync();
            throw;
        }
    }

    public async Task ProcessExpiredFundsAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var expiredFunds = await db.WalletFunds
            .Include(f => f.Recipients)
            .Where(f => f.Status == Shared.Models.FundStatus.Created ||
                        f.Status == Shared.Models.FundStatus.PartiallyReceived)
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
                var creatorWallet = await wat.GetAccountWalletAsync(fund.CreatorAccountId);
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

    public async Task<SnWalletFund?> GetWalletFundAsync(Guid fundId)
    {
        var fund = await db.WalletFunds
            .Include(f => f.Recipients)
            .FirstOrDefaultAsync(f => f.Id == fundId);

        return fund;
    }

    public async Task<WalletOverview> GetWalletOverviewAsync(Guid accountId, DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var wallet = await wat.GetAccountWalletAsync(accountId);
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

    private string GetTranslationForCurrency(string currency, string locale)
    {
        switch (currency)
        {
            case WalletCurrency.SourcePoint:
                return localizer.Get("currencyPoints", locale);
            case WalletCurrency.GoldenPoint:
                return localizer.Get("currencyGolds", locale);
        }

        return "unknown";
    }

    private static decimal TruncateToThreeDecimals(decimal amount)
    {
        return Math.Truncate(amount * 1000) / 1000;
    }

    // --- Transaction Lifecycle Methods ---

    public async Task<SnWalletTransaction> ConfirmTransactionAsync(Guid transactionId, Guid payeeAccountId)
    {
        await using var transactionScope = await db.Database.BeginTransactionAsync();

        try
        {
            var transaction = await db.PaymentTransactions
                .FirstOrDefaultAsync(t => t.Id == transactionId)
                ?? throw new InvalidOperationException("Transaction not found");

            if (transaction.Status != Shared.Models.TransactionStatus.Pending &&
                transaction.Status != Shared.Models.TransactionStatus.Frozen)
                throw new InvalidOperationException($"Transaction is in status {transaction.Status}, cannot confirm");

            if (!transaction.ExpiresAt.HasValue || transaction.ExpiresAt.Value < SystemClock.Instance.GetCurrentInstant())
                throw new InvalidOperationException("Transaction has expired");

            // Verify payee is the intended recipient
            if (transaction.PayeeWalletId.HasValue)
            {
                var payeeWallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == transaction.PayeeWalletId.Value);
                if (payeeWallet?.AccountId != payeeAccountId)
                    throw new InvalidOperationException("You are not the payee of this transaction");
            }

            var now = SystemClock.Instance.GetCurrentInstant();

            // Release held amount from payer
            if (transaction.PayerWalletId.HasValue)
            {
                await db.WalletPockets
                    .Where(p => p.WalletId == transaction.PayerWalletId.Value && p.Currency == transaction.Currency)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.HeldAmount, p => p.HeldAmount - transaction.Amount));
            }

            // Credit payee
            if (transaction.PayeeWalletId.HasValue)
            {
                var (payeePocket, isNewlyCreated) =
                    await wat.GetOrCreateWalletPocketAsync(transaction.PayeeWalletId.Value, transaction.Currency, transaction.Amount);

                if (!isNewlyCreated)
                    await db.WalletPockets
                        .Where(p => p.Id == payeePocket.Id)
                        .ExecuteUpdateAsync(s =>
                            s.SetProperty(p => p.Amount, p => p.Amount + transaction.Amount));
            }

            transaction.Status = Shared.Models.TransactionStatus.Confirmed;
            transaction.ConfirmedAt = now;
            await db.SaveChangesAsync();
            await transactionScope.CommitAsync();

            // Send real-time WebSocket updates
            await NotifyTransactionStatusChange(transaction,
                Shared.Models.WebSocketPacketType.WalletTransactionConfirmed,
                transaction.PayerWalletId.HasValue
                    ? await db.Wallets.Where(w => w.Id == transaction.PayerWalletId.Value).Select(w => w.AccountId).FirstOrDefaultAsync()
                    : null,
                payeeAccountId);

            if (transaction.PayeeWalletId.HasValue)
                await NotifyPocketUpdated(transaction.PayeeWalletId.Value, transaction.Currency, 0, 0);

            return transaction;
        }
        catch
        {
            await transactionScope.RollbackAsync();
            throw;
        }
    }

    public async Task<SnWalletTransaction> RejectTransactionAsync(Guid transactionId, Guid payeeAccountId)
    {
        await using var transactionScope = await db.Database.BeginTransactionAsync();

        try
        {
            var transaction = await db.PaymentTransactions
                .FirstOrDefaultAsync(t => t.Id == transactionId)
                ?? throw new InvalidOperationException("Transaction not found");

            if (transaction.Status != Shared.Models.TransactionStatus.Pending &&
                transaction.Status != Shared.Models.TransactionStatus.Frozen)
                throw new InvalidOperationException($"Transaction is in status {transaction.Status}, cannot reject");

            // Verify payee is the intended recipient
            if (transaction.PayeeWalletId.HasValue)
            {
                var payeeWallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == transaction.PayeeWalletId.Value);
                if (payeeWallet?.AccountId != payeeAccountId)
                    throw new InvalidOperationException("You are not the payee of this transaction");
            }

            // Refund: release held amount back to payer
            if (transaction.PayerWalletId.HasValue)
            {
                // Release held amount
                await db.WalletPockets
                    .Where(p => p.WalletId == transaction.PayerWalletId.Value && p.Currency == transaction.Currency)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.HeldAmount, p => p.HeldAmount - transaction.Amount));

                // Return funds to payer pocket
                await db.WalletPockets
                    .Where(p => p.WalletId == transaction.PayerWalletId.Value && p.Currency == transaction.Currency)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.Amount, p => p.Amount + transaction.Amount));
            }

            transaction.Status = Shared.Models.TransactionStatus.Refunded;
            await db.SaveChangesAsync();
            await transactionScope.CommitAsync();

            // Send real-time WebSocket updates
            await NotifyTransactionStatusChange(transaction,
                Shared.Models.WebSocketPacketType.WalletTransactionRefunded,
                transaction.PayerWalletId.HasValue
                    ? await db.Wallets.Where(w => w.Id == transaction.PayerWalletId.Value).Select(w => w.AccountId).FirstOrDefaultAsync()
                    : null,
                payeeAccountId);

            if (transaction.PayerWalletId.HasValue)
                await NotifyPocketUpdated(transaction.PayerWalletId.Value, transaction.Currency, 0, 0);

            return transaction;
        }
        catch
        {
            await transactionScope.RollbackAsync();
            throw;
        }
    }

    public async Task ProcessExpiredTransactionsAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        // Find all expired pending/frozen transactions
        var expiredTransactions = await db.PaymentTransactions
            .Where(t => (t.Status == Shared.Models.TransactionStatus.Pending ||
                         t.Status == Shared.Models.TransactionStatus.Frozen) &&
                        t.ExpiresAt.HasValue && t.ExpiresAt.Value < now)
            .ToListAsync();

        foreach (var transaction in expiredTransactions)
        {
            try
            {
                if (transaction.IsFrozen && !transaction.RequireConfirmation)
                {
                    // Frozen-only: auto-release to payee
                    await ReleaseTransactionAsync(transaction);
                }
                else if (transaction.RequireConfirmation)
                {
                    // Confirm-required (with or without freeze): auto-refund to payer
                    await RefundExpiredTransactionAsync(transaction);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing expired transaction {TransactionId}", transaction.Id);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ReleaseTransactionAsync(SnWalletTransaction transaction)
    {
        await using var transactionScope = await db.Database.BeginTransactionAsync();

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();

            // Release held amount from payer
            if (transaction.PayerWalletId.HasValue)
            {
                await db.WalletPockets
                    .Where(p => p.WalletId == transaction.PayerWalletId.Value && p.Currency == transaction.Currency)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.HeldAmount, p => p.HeldAmount - transaction.Amount));
            }

            // Credit payee
            if (transaction.PayeeWalletId.HasValue)
            {
                var (payeePocket, isNewlyCreated) =
                    await wat.GetOrCreateWalletPocketAsync(transaction.PayeeWalletId.Value, transaction.Currency, transaction.Amount);

                if (!isNewlyCreated)
                    await db.WalletPockets
                        .Where(p => p.Id == payeePocket.Id)
                        .ExecuteUpdateAsync(s =>
                            s.SetProperty(p => p.Amount, p => p.Amount + transaction.Amount));
            }

            transaction.Status = Shared.Models.TransactionStatus.Confirmed;
            transaction.ConfirmedAt = now;
            await db.SaveChangesAsync();
            await transactionScope.CommitAsync();

            // Resolve account IDs for WebSocket notifications
            var payerAccountId = transaction.PayerWalletId.HasValue
                ? await db.Wallets.Where(w => w.Id == transaction.PayerWalletId.Value).Select(w => w.AccountId).FirstOrDefaultAsync()
                : null;
            var payeeAccountId = transaction.PayeeWalletId.HasValue
                ? await db.Wallets.Where(w => w.Id == transaction.PayeeWalletId.Value).Select(w => w.AccountId).FirstOrDefaultAsync()
                : null;

            await NotifyTransactionStatusChange(transaction,
                Shared.Models.WebSocketPacketType.WalletTransactionConfirmed,
                payerAccountId, payeeAccountId);

            if (transaction.PayeeWalletId.HasValue)
                await NotifyPocketUpdated(transaction.PayeeWalletId.Value, transaction.Currency, 0, 0);
        }
        catch
        {
            await transactionScope.RollbackAsync();
            throw;
        }
    }

    private async Task RefundExpiredTransactionAsync(SnWalletTransaction transaction)
    {
        await using var transactionScope = await db.Database.BeginTransactionAsync();

        try
        {
            // Release held amount and return to payer
            if (transaction.PayerWalletId.HasValue)
            {
                // Release held amount
                await db.WalletPockets
                    .Where(p => p.WalletId == transaction.PayerWalletId.Value && p.Currency == transaction.Currency)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.HeldAmount, p => p.HeldAmount - transaction.Amount));

                // Return funds to payer pocket
                await db.WalletPockets
                    .Where(p => p.WalletId == transaction.PayerWalletId.Value && p.Currency == transaction.Currency)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(p => p.Amount, p => p.Amount + transaction.Amount));
            }

            transaction.Status = Shared.Models.TransactionStatus.Refunded;
            await db.SaveChangesAsync();
            await transactionScope.CommitAsync();

            // Resolve account IDs for WebSocket notifications
            var payerAccountId = transaction.PayerWalletId.HasValue
                ? await db.Wallets.Where(w => w.Id == transaction.PayerWalletId.Value).Select(w => w.AccountId).FirstOrDefaultAsync()
                : null;
            var payeeAccountId = transaction.PayeeWalletId.HasValue
                ? await db.Wallets.Where(w => w.Id == transaction.PayeeWalletId.Value).Select(w => w.AccountId).FirstOrDefaultAsync()
                : null;

            await NotifyTransactionStatusChange(transaction,
                Shared.Models.WebSocketPacketType.WalletTransactionExpired,
                payerAccountId, payeeAccountId);

            if (transaction.PayerWalletId.HasValue)
                await NotifyPocketUpdated(transaction.PayerWalletId.Value, transaction.Currency, 0, 0);
        }
        catch
        {
            await transactionScope.RollbackAsync();
            throw;
        }
    }

    // --- Fund Raising Methods ---

    public async Task<SnWalletFund> CreateRaisingFundAsync(
        Guid creatorAccountId,
        string currency,
        decimal targetAmount,
        int maxParticipants,
        Shared.Models.ContributionType contributionType,
        decimal contributionAmount,
        bool isOpen,
        List<Guid>? invitedAccountIds = null,
        string? message = null,
        Duration? expiration = null,
        Instant? deadlineAt = null)
    {
        if (targetAmount <= 0)
            throw new ArgumentException("Target amount must be positive");

        if (contributionType == Shared.Models.ContributionType.Fixed && contributionAmount <= 0)
            throw new ArgumentException("Fixed contribution amount must be positive");

        var now = SystemClock.Instance.GetCurrentInstant();

        var fund = new SnWalletFund
        {
            CreatorAccountId = creatorAccountId,
            Currency = currency,
            TotalAmount = TruncateToThreeDecimals(targetAmount),
            RemainingAmount = TruncateToThreeDecimals(targetAmount),
            AmountOfSplits = maxParticipants,
            SplitType = Shared.Models.FundSplitType.Even,
            Message = message,
            ExpiredAt = now.Plus(expiration ?? Duration.FromHours(24)),
            IsOpen = isOpen,
            IsRaising = true,
            TargetAmount = TruncateToThreeDecimals(targetAmount),
            ContributionType = contributionType,
            ContributionAmount = contributionType == Shared.Models.ContributionType.Fixed
                ? TruncateToThreeDecimals(contributionAmount)
                : 0,
            DeadlineAt = deadlineAt,
            Recipients = invitedAccountIds?.Select(accountId => new SnWalletFundRecipient
            {
                RecipientAccountId = accountId,
                Amount = 0
            }).ToList() ?? new List<SnWalletFundRecipient>()
        };

        db.WalletFunds.Add(fund);
        await db.SaveChangesAsync();

        return fund;
    }

    public async Task<SnWalletTransaction> ContributeToFundAsync(
        Guid contributorAccountId,
        Guid fundId,
        decimal amount)
    {
        await using var transactionScope = await db.Database.BeginTransactionAsync();

        try
        {
            var fund = await db.WalletFunds
                .Include(f => f.Recipients)
                .FirstOrDefaultAsync(f => f.Id == fundId)
                ?? throw new InvalidOperationException("Fund not found");

            if (!fund.IsRaising)
                throw new InvalidOperationException("This fund is not in raising mode");

            if (fund.Status is Shared.Models.FundStatus.Expired or Shared.Models.FundStatus.FullyReceived)
                throw new InvalidOperationException("Fund is no longer accepting contributions");

            if (fund.DeadlineAt.HasValue && fund.DeadlineAt.Value < SystemClock.Instance.GetCurrentInstant())
                throw new InvalidOperationException("Fund deadline has passed");

            // Check if contributor already contributed
            var existingContributor = fund.Recipients.FirstOrDefault(r => r.RecipientAccountId == contributorAccountId);
            if (existingContributor != null)
                throw new InvalidOperationException("You have already contributed to this fund");

            // Determine contribution amount
            decimal contributionAmount;
            if (fund.ContributionType == Shared.Models.ContributionType.Fixed)
            {
                contributionAmount = fund.ContributionAmount;
            }
            else
            {
                // Free contribution: use provided amount
                if (amount <= 0)
                    throw new ArgumentException("Contribution amount must be positive");
                contributionAmount = TruncateToThreeDecimals(amount);
            }

            // Check if contribution would exceed target
            if (fund.TargetAmount > 0)
            {
                var currentRaised = fund.Recipients.Where(r => r.IsReceived).Sum(r => r.Amount);
                if (currentRaised + contributionAmount > fund.TargetAmount)
                    throw new InvalidOperationException("Contribution would exceed target amount");
            }

            // Check participant limit
            if (!fund.IsOpen && fund.AmountOfSplits > 0)
            {
                var currentParticipants = fund.Recipients.Count;
                if (currentParticipants >= fund.AmountOfSplits)
                    throw new InvalidOperationException("Fund has reached maximum participants");

                // For closed funds, check if contributor is invited
                var isInvited = fund.Recipients.Any(r => r.RecipientAccountId == contributorAccountId);
                if (!isInvited)
                    throw new InvalidOperationException("You are not invited to contribute to this fund");
            }

            // Deduct from contributor's wallet
            var contributorWallet = await wat.GetAccountWalletAsync(contributorAccountId)
                ?? throw new InvalidOperationException("Contributor wallet not found");

            var (contributorPocket, isNewlyCreated) =
                await wat.GetOrCreateWalletPocketAsync(contributorWallet.Id, fund.Currency);

            if (isNewlyCreated || contributorPocket.Amount < contributionAmount)
                throw new InvalidOperationException("Insufficient funds");

            await db.WalletPockets
                .Where(p => p.Id == contributorPocket.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(p => p.Amount, p => p.Amount - contributionAmount));

            // Create or update contributor record
            if (existingContributor != null)
            {
                // This shouldn't happen since we checked above, but just in case
                existingContributor.Amount = contributionAmount;
                existingContributor.IsReceived = true;
                existingContributor.ReceivedAt = SystemClock.Instance.GetCurrentInstant();
            }
            else
            {
                var contributor = new SnWalletFundRecipient
                {
                    FundId = fundId,
                    RecipientAccountId = contributorAccountId,
                    Amount = contributionAmount,
                    IsReceived = true,
                    ReceivedAt = SystemClock.Instance.GetCurrentInstant()
                };
                db.WalletFundRecipients.Add(contributor);
            }

            // Update fund remaining amount
            fund.RemainingAmount -= contributionAmount;

            // Check if target reached
            var totalRaised = fund.Recipients.Where(r => r.IsReceived).Sum(r => r.Amount) + contributionAmount;
            if (fund.TargetAmount > 0 && totalRaised >= fund.TargetAmount)
            {
                fund.Status = Shared.Models.FundStatus.FullyReceived;
            }
            else
            {
                fund.Status = Shared.Models.FundStatus.PartiallyReceived;
            }

            await db.SaveChangesAsync();
            await transactionScope.CommitAsync();

            // Notify fund creator and contributor via WebSocket
            var fundPayload = new
            {
                fund_id = fund.Id.ToString(),
                contributor_account_id = contributorAccountId.ToString(),
                amount = contributionAmount,
                currency = fund.Currency,
                raised_amount = fund.Recipients.Where(r => r.IsReceived).Sum(r => r.Amount),
                target_amount = fund.TargetAmount,
                status = fund.Status
            };

            await SendWalletWebSocketPacket(contributorAccountId,
                Shared.Models.WebSocketPacketType.WalletFundContributed, fundPayload);
            await SendWalletWebSocketPacket(fund.CreatorAccountId,
                Shared.Models.WebSocketPacketType.WalletFundContributed, fundPayload);

            // Notify pocket balance update for contributor
            await NotifyPocketUpdated(contributorWallet.Id, fund.Currency, 0, 0);

            if (fund.Status == Shared.Models.FundStatus.FullyReceived)
            {
                await SendWalletWebSocketPacket(fund.CreatorAccountId,
                    Shared.Models.WebSocketPacketType.WalletFundCompleted, fundPayload);
            }

            // Create a system transaction for the contribution
            var transaction = await CreateTransactionAsync(
                payerWalletId: null, // System credit
                payeeWalletId: null, // No direct payee - funds go to the fund pool
                currency: fund.Currency,
                amount: contributionAmount,
                remarks: $"Contribution to fund {fund.Id}",
                type: Shared.Models.TransactionType.System,
                silent: true
            );

            return transaction;
        }
        catch
        {
            await transactionScope.RollbackAsync();
            throw;
        }
    }

    public async Task ProcessExpiredRaisingFundsAsync()
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        var expiredFunds = await db.WalletFunds
            .Include(f => f.Recipients)
            .Where(f => f.IsRaising &&
                        (f.Status == Shared.Models.FundStatus.Created ||
                         f.Status == Shared.Models.FundStatus.PartiallyReceived) &&
                        f.DeadlineAt.HasValue && f.DeadlineAt.Value < now)
            .ToListAsync();

        foreach (var fund in expiredFunds)
        {
            try
            {
                var totalRaised = fund.Recipients.Where(r => r.IsReceived).Sum(r => r.Amount);

                if (fund.TargetAmount > 0 && totalRaised >= fund.TargetAmount)
                {
                    // Target reached - mark as fully received
                    fund.Status = Shared.Models.FundStatus.FullyReceived;
                }
                else
                {
                    // Target not reached - refund all contributions
                    fund.Status = Shared.Models.FundStatus.Expired;

                    foreach (var contributor in fund.Recipients.Where(r => r.IsReceived))
                    {
                        var contributorWallet = await wat.GetAccountWalletAsync(contributor.RecipientAccountId);
                        if (contributorWallet != null)
                        {
                            await CreateTransactionAsync(
                                payerWalletId: null, // System refund
                                payeeWalletId: contributorWallet.Id,
                                currency: fund.Currency,
                                amount: contributor.Amount,
                                remarks: $"Refund for expired raising fund {fund.Id}",
                                type: Shared.Models.TransactionType.System,
                                silent: true
                            );
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing expired raising fund {FundId}", fund.Id);
            }
        }

        await db.SaveChangesAsync();
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
