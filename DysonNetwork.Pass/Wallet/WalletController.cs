using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Permission;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Wallet;

[ApiController]
[Route("/api/wallets")]
public class WalletController(AppDatabase db, WalletService ws, PaymentService payment) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Wallet>> CreateWallet()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        try
        {
            var wallet = await ws.CreateWalletAsync(currentUser.Id);
            return Ok(wallet);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<Wallet>> GetWallet()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var wallet = await ws.GetWalletAsync(currentUser.Id);
        if (wallet is null) return NotFound("Wallet was not found, please create one first.");
        return Ok(wallet);
    }

    [HttpGet("transactions")]
    [Authorize]
    public async Task<ActionResult<List<Transaction>>> GetTransactions(
        [FromQuery] int offset = 0, [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var accountWallet = await db.Wallets.Where(w => w.AccountId == currentUser.Id).FirstOrDefaultAsync();
        if (accountWallet is null) return NotFound();

        var query = db.PaymentTransactions
            .Where(t => t.PayeeWalletId == accountWallet.Id || t.PayerWalletId == accountWallet.Id)
            .AsQueryable();

        var transactionCount = await query.CountAsync();
        Response.Headers["X-Total"] = transactionCount.ToString();
        
        var transactions = await query
            .Skip(offset)
            .Take(take)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(transactions);
    }

    [HttpGet("orders")]
    [Authorize]
    public async Task<ActionResult<List<Order>>> GetOrders(
        [FromQuery] int offset = 0, [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        
        var accountWallet = await db.Wallets.Where(w => w.AccountId == currentUser.Id).FirstOrDefaultAsync();
        if (accountWallet is null) return NotFound();
        
        var query = db.PaymentOrders.AsQueryable()
            .Include(o => o.Transaction)
            .Where(o => o.Transaction != null && (o.Transaction.PayeeWalletId == accountWallet.Id || o.Transaction.PayerWalletId == accountWallet.Id))
            .AsQueryable();
        
        var orderCount = await query.CountAsync();
        Response.Headers["X-Total"] = orderCount.ToString();
        
        var orders = await query
            .Skip(offset)
            .Take(take)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(orders);
    }

    public class WalletBalanceRequest
    {
        public string? Remark { get; set; }
        [Required] public decimal Amount { get; set; }
        [Required] public string Currency { get; set; } = null!;
        [Required] public Guid AccountId { get; set; }
    }

    [HttpPost("balance")]
    [Authorize]
    [RequiredPermission("maintenance", "wallets.balance.modify")]
    public async Task<ActionResult<Transaction>> ModifyWalletBalance([FromBody] WalletBalanceRequest request)
    {
        var wallet = await ws.GetWalletAsync(request.AccountId);
        if (wallet is null) return NotFound("Wallet was not found.");

        var transaction = request.Amount >= 0
            ? await payment.CreateTransactionAsync(
                payerWalletId: null,
                payeeWalletId: wallet.Id,
                currency: request.Currency,
                amount: request.Amount,
                remarks: request.Remark
            )
            : await payment.CreateTransactionAsync(
                payerWalletId: wallet.Id,
                payeeWalletId: null,
                currency: request.Currency,
                amount: request.Amount,
                remarks: request.Remark
            );

        return Ok(transaction);
    }
}