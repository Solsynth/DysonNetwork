using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/admin/payments")]
[Authorize]
public class PaymentAdminController(
    AppDatabase db,
    PaymentService payment,
    WalletService wallets,
    RemoteAccountService remoteAccounts
) : ControllerBase
{
    public class AdminWalletBalanceRequest
    {
        public string? Remark { get; set; }
        [Required] public decimal Amount { get; set; }
        [Required] public string Currency { get; set; } = null!;
        public Guid? AccountId { get; set; }
        public Guid? WalletId { get; set; }
        public bool ForceOperation { get; set; }
    }

    [HttpGet("transactions")]
    [AskPermission(PermissionKeys.WalletsTransactionsManage)]
    public async Task<ActionResult<List<SnWalletTransaction>>> ListTransactions(
        [FromQuery] Guid? walletId = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] TransactionStatus? status = null,
        [FromQuery] TransactionType? type = null,
        [FromQuery] string? currency = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var walletIds = await ResolveWalletIdsAsync(accountId, walletId, HttpContext.RequestAborted);
        if (accountId.HasValue && walletIds.Count == 0)
            return NotFound(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Wallet was not found for the specified account.", Status = 404 });
        if (walletId.HasValue && walletIds.Count == 0)
            return NotFound(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Wallet was not found.", Status = 404 });

        var query = db.PaymentTransactions
            .AsNoTracking()
            .Include(t => t.PayerWallet)
            .Include(t => t.PayeeWallet)
            .AsQueryable();

        if (walletIds.Count > 0)
        {
            query = query.Where(t =>
                (t.PayerWalletId.HasValue && walletIds.Contains(t.PayerWalletId.Value)) ||
                (t.PayeeWalletId.HasValue && walletIds.Contains(t.PayeeWalletId.Value)));
        }

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        if (type.HasValue)
            query = query.Where(t => t.Type == type.Value);
        if (!string.IsNullOrWhiteSpace(currency))
            query = query.Where(t => t.Currency == currency);

        var total = await query.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var items = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        await HydrateWalletAccountsAsync(items);
        return Ok(items);
    }

    [HttpGet("transactions/{id:guid}")]
    [AskPermission(PermissionKeys.WalletsTransactionsManage)]
    public async Task<ActionResult<SnWalletTransaction>> GetTransaction(Guid id)
    {
        var transaction = await db.PaymentTransactions
            .AsNoTracking()
            .Include(t => t.PayerWallet)
            .Include(t => t.PayeeWallet)
            .FirstOrDefaultAsync(t => t.Id == id, HttpContext.RequestAborted);

        if (transaction is null)
            return NotFound(new ApiError { Code = "WALLET_TRANSACTION_NOT_FOUND", Message = "Transaction was not found.", Status = 404 });

        await HydrateWalletAccountsAsync([transaction]);
        return Ok(transaction);
    }

    [HttpGet("orders")]
    [AskPermission(PermissionKeys.OrdersView)]
    public async Task<ActionResult<List<SnWalletOrder>>> ListOrders(
        [FromQuery] Guid? walletId = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] string? appIdentifier = null,
        [FromQuery] string? productIdentifier = null,
        [FromQuery] string? currency = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(0, offset);

        var walletIds = await ResolveWalletIdsAsync(accountId, walletId, HttpContext.RequestAborted);
        if (accountId.HasValue && walletIds.Count == 0)
            return NotFound(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Wallet was not found for the specified account.", Status = 404 });
        if (walletId.HasValue && walletIds.Count == 0)
            return NotFound(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Wallet was not found.", Status = 404 });

        var query = db.PaymentOrders
            .AsNoTracking()
            .Include(o => o.Transaction)
            .Include(o => o.Items)
            .Include(o => o.PayeeWallet)
            .AsQueryable();

        if (walletIds.Count > 0)
        {
            query = query.Where(o =>
                (o.PayeeWalletId.HasValue && walletIds.Contains(o.PayeeWalletId.Value)) ||
                (o.Transaction != null && o.Transaction.PayerWalletId.HasValue && walletIds.Contains(o.Transaction.PayerWalletId.Value)) ||
                (o.Transaction != null && o.Transaction.PayeeWalletId.HasValue && walletIds.Contains(o.Transaction.PayeeWalletId.Value)));
        }

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(appIdentifier))
            query = query.Where(o => o.AppIdentifier == appIdentifier);
        if (!string.IsNullOrWhiteSpace(productIdentifier))
            query = query.Where(o => o.ProductIdentifier == productIdentifier);
        if (!string.IsNullOrWhiteSpace(currency))
            query = query.Where(o => o.Currency == currency);

        var total = await query.CountAsync(HttpContext.RequestAborted);
        Response.Headers.Append("X-Total", total.ToString());

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(HttpContext.RequestAborted);

        return Ok(items);
    }

    [HttpGet("orders/{id:guid}")]
    [AskPermission(PermissionKeys.OrdersView)]
    public async Task<ActionResult<SnWalletOrder>> GetOrder(Guid id)
    {
        var order = await db.PaymentOrders
            .AsNoTracking()
            .Include(o => o.Transaction)
            .Include(o => o.Items)
            .Include(o => o.PayeeWallet)
            .FirstOrDefaultAsync(o => o.Id == id, HttpContext.RequestAborted);

        if (order is null)
            return NotFound(new ApiError { Code = "WALLET_ORDER_NOT_FOUND", Message = "Order was not found.", Status = 404 });

        return Ok(order);
    }

    [HttpPost("balance")]
    [AskPermission(PermissionKeys.WalletsBalanceModify)]
    public async Task<ActionResult<SnWalletTransaction>> ModifyWalletBalance([FromBody] AdminWalletBalanceRequest request)
    {
        if (request.WalletId is null && request.AccountId is null)
            return BadRequest(new ApiError { Code = "WALLET_MISSING_TARGET", Message = "You must specify either WalletId or AccountId.", Status = 400 });

        var wallet = request.AccountId is not null
            ? await wallets.GetAccountWalletAsync(request.AccountId.Value)
            : await wallets.GetWalletAsync(request.WalletId!.Value);
        if (wallet is null)
            return NotFound(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Wallet was not found.", Status = 404 });

        try
        {
            var transaction = request.Amount >= 0
                ? await payment.CreateTransactionAsync(
                    payerWalletId: null,
                    payeeWalletId: wallet.Id,
                    currency: request.Currency,
                    amount: request.Amount,
                    remarks: request.Remark,
                    force: request.ForceOperation
                )
                : await payment.CreateTransactionAsync(
                    payerWalletId: wallet.Id,
                    payeeWalletId: null,
                    currency: request.Currency,
                    amount: -request.Amount,
                    remarks: request.Remark,
                    force: request.ForceOperation
                );

            return Ok(transaction);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_BALANCE_MODIFY_FAILED", Message = ex.Message, Status = 400 });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_BALANCE_MODIFY_FAILED", Message = ex.Message, Status = 400 });
        }
    }

    private async Task<HashSet<Guid>> ResolveWalletIdsAsync(
        Guid? accountId,
        Guid? walletId,
        CancellationToken cancellationToken
    )
    {
        if (walletId.HasValue)
        {
            var wallet = await db.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == walletId.Value, cancellationToken);
            return wallet is null ? [] : [wallet.Id];
        }

        if (!accountId.HasValue)
            return [];

        return await db.Wallets
            .AsNoTracking()
            .Where(w => w.AccountId == accountId.Value)
            .Select(w => w.Id)
            .ToHashSetAsync(cancellationToken);
    }

    private async Task HydrateWalletAccountsAsync(IEnumerable<SnWalletTransaction> transactions)
    {
        var accountIds = transactions
            .SelectMany(t => new[] { t.PayerWallet?.AccountId, t.PayeeWallet?.AccountId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        if (accountIds.Count == 0)
            return;

        var accounts = await remoteAccounts.GetAccountBatch(accountIds);
        foreach (var transaction in transactions)
        {
            if (transaction.PayerWallet?.AccountId is Guid payerAccountId)
            {
                var payer = accounts.FirstOrDefault(x => x.Id == payerAccountId.ToString());
                if (payer is not null)
                    transaction.PayerWallet.Account = SnAccount.FromProtoValue(payer);
            }

            if (transaction.PayeeWallet?.AccountId is Guid payeeAccountId)
            {
                var payee = accounts.FirstOrDefault(x => x.Id == payeeAccountId.ToString());
                if (payee is not null)
                    transaction.PayeeWallet.Account = SnAccount.FromProtoValue(payee);
            }
        }
    }
}
