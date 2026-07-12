using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/admin/stats")]
[Authorize]
public class WalletStatsAdminController(AppDatabase db) : ControllerBase
{
    public class WalletStatsResponse
    {
        public Instant CalculatedAt { get; set; }
        public long TotalWallets { get; set; }
        public long TotalTransactions { get; set; }
        public long ConfirmedTransactions { get; set; }
        public long PendingTransactions { get; set; }
        public long TransactionsLastDay { get; set; }
        public long TransactionsLastWeek { get; set; }
        public long TransactionsLastMonth { get; set; }
        public long TotalOrders { get; set; }
        public long PaidOrders { get; set; }
        public long TotalSubscriptions { get; set; }
    }

    [HttpGet]
    [AskPermission(PermissionKeys.WalletsTransactionsManage)]
    public async Task<ActionResult<WalletStatsResponse>> GetStats(CancellationToken cancellationToken)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var oneDayAgo = now - Duration.FromDays(1);
        var sevenDaysAgo = now - Duration.FromDays(7);
        var thirtyDaysAgo = now - Duration.FromDays(30);
        var transactions = db.PaymentTransactions.AsNoTracking();

        return Ok(new WalletStatsResponse
        {
            CalculatedAt = now,
            TotalWallets = await db.Wallets.AsNoTracking().LongCountAsync(cancellationToken),
            TotalTransactions = await transactions.LongCountAsync(cancellationToken),
            ConfirmedTransactions = await transactions.LongCountAsync(t => t.Status == TransactionStatus.Confirmed, cancellationToken),
            PendingTransactions = await transactions.LongCountAsync(t => t.Status == TransactionStatus.Pending, cancellationToken),
            TransactionsLastDay = await transactions.LongCountAsync(t => t.CreatedAt >= oneDayAgo, cancellationToken),
            TransactionsLastWeek = await transactions.LongCountAsync(t => t.CreatedAt >= sevenDaysAgo, cancellationToken),
            TransactionsLastMonth = await transactions.LongCountAsync(t => t.CreatedAt >= thirtyDaysAgo, cancellationToken),
            TotalOrders = await db.PaymentOrders.AsNoTracking().LongCountAsync(cancellationToken),
            PaidOrders = await db.PaymentOrders.AsNoTracking().LongCountAsync(o => o.Status == OrderStatus.Paid, cancellationToken),
            TotalSubscriptions = await db.WalletSubscriptions.AsNoTracking().LongCountAsync(cancellationToken)
        });
    }
}
