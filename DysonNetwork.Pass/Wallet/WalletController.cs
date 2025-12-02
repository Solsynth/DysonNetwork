using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Auth;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Pass.Wallet;

[ApiController]
[Route("/api/wallets")]
public class WalletController(
    AppDatabase db,
    WalletService ws,
    PaymentService payment,
    AuthService auth,
    ICacheService cache,
    ILogger<WalletController> logger
) : ControllerBase
{
    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnWallet>> CreateWallet()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

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
    public async Task<ActionResult<SnWallet>> GetWallet()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var wallet = await ws.GetWalletAsync(currentUser.Id);
        if (wallet is null) return NotFound("Wallet was not found, please create one first.");
        return Ok(wallet);
    }

    public class WalletStats
    {
        public Instant PeriodBegin { get; set; }
        public Instant PeriodEnd { get; set; }
        public int TotalTransactions { get; set; }
        public int TotalOrders { get; set; }
        public Dictionary<string, decimal> IncomeCatgories { get; set; } = null!;
        public Dictionary<string, decimal> OutgoingCategories { get; set; } = null!;
        public decimal TotalIncome => IncomeCatgories.Values.Sum();
        public decimal TotalOutgoing => OutgoingCategories.Values.Sum();
        public decimal Sum => TotalIncome - TotalOutgoing;
    }

    [HttpGet("stats")]
    [Authorize]
    public async Task<ActionResult<WalletStats>> GetWalletStats([FromQuery] int period = 30)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var wallet = await ws.GetWalletAsync(currentUser.Id);
        if (wallet is null) return NotFound("Wallet was not found, please create one first.");

        var periodEnd = SystemClock.Instance.GetCurrentInstant();
        var periodBegin = periodEnd.Minus(Duration.FromDays(period));

        var cacheKey = $"wallet:stats:{currentUser.Id}:{period}";
        var cached = await cache.GetAsync<WalletStats>(cacheKey);
        if (cached != null)
        {
            return Ok(cached);
        }

        var transactions = await db.PaymentTransactions
            .Where(t => (t.PayerWalletId == wallet.Id || t.PayeeWalletId == wallet.Id) &&
                        t.CreatedAt >= periodBegin && t.CreatedAt <= periodEnd)
            .ToListAsync();

        var orders = await db.PaymentOrders
            .Where(o => o.PayeeWalletId == wallet.Id &&
                        o.CreatedAt >= periodBegin && o.CreatedAt <= periodEnd)
            .ToListAsync();

        var incomeCategories = transactions
            .Where(t => t.PayeeWalletId == wallet.Id)
            .GroupBy(t => t.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var outgoingCategories = transactions
            .Where(t => t.PayerWalletId == wallet.Id)
            .GroupBy(t => t.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var stats = new WalletStats
        {
            PeriodBegin = periodBegin,
            PeriodEnd = periodEnd,
            TotalTransactions = transactions.Count,
            TotalOrders = orders.Count,
            IncomeCatgories = incomeCategories,
            OutgoingCategories = outgoingCategories
        };

        await cache.SetAsync(cacheKey, stats, TimeSpan.FromHours(1));
        return Ok(stats);
    }

    [HttpGet("transactions")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletTransaction>>> GetTransactions(
        [FromQuery] int offset = 0, [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var accountWallet = await db.Wallets.Where(w => w.AccountId == currentUser.Id).FirstOrDefaultAsync();
        if (accountWallet is null) return NotFound();

        var query = db.PaymentTransactions
            .Where(t => t.PayeeWalletId == accountWallet.Id || t.PayerWalletId == accountWallet.Id)
            .OrderByDescending(t => t.CreatedAt)
            .AsQueryable();

        var transactionCount = await query.CountAsync();
        Response.Headers["X-Total"] = transactionCount.ToString();

        var transactions = await query
            .Skip(offset)
            .Take(take)
            .Include(t => t.PayerWallet)
            .ThenInclude(w => w.Account)
            .ThenInclude(w => w.Profile)
            .Include(t => t.PayeeWallet)
            .ThenInclude(w => w.Account)
            .ThenInclude(w => w.Profile)
            .ToListAsync();

        return Ok(transactions);
    }

    [HttpGet("orders")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletOrder>>> GetOrders(
        [FromQuery] int offset = 0, [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var accountWallet = await db.Wallets.Where(w => w.AccountId == currentUser.Id).FirstOrDefaultAsync();
        if (accountWallet is null) return NotFound();

        var query = db.PaymentOrders.AsQueryable()
            .Include(o => o.Transaction)
            .Where(o => o.Transaction != null && (o.Transaction.PayeeWalletId == accountWallet.Id ||
                                                  o.Transaction.PayerWalletId == accountWallet.Id))
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

    public class WalletTransferRequest
    {
        public string? Remark { get; set; }
        [Required] public decimal Amount { get; set; }
        [Required] public string Currency { get; set; } = null!;
        [Required] public Guid PayeeAccountId { get; set; }
        [Required] public string PinCode { get; set; } = null!;
    }

    [HttpPost("balance")]
    [Authorize]
    [AskPermission("wallets.balance.modify")]
    public async Task<ActionResult<SnWalletTransaction>> ModifyWalletBalance([FromBody] WalletBalanceRequest request)
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

    [HttpPost("transfer")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> Transfer([FromBody] WalletTransferRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        // Validate PIN code
        if (!await auth.ValidatePinCode(currentUser.Id, request.PinCode))
            return StatusCode(403, "Invalid PIN Code");

        if (currentUser.Id == request.PayeeAccountId) return BadRequest("Cannot transfer to yourself.");

        try
        {
            var transaction = await payment.TransferAsync(
                payerAccountId: currentUser.Id,
                payeeAccountId: request.PayeeAccountId,
                currency: request.Currency,
                amount: request.Amount
            );

            return Ok(transaction);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }

    public class CreateFundRequest
    {
        [Required] public List<Guid> RecipientAccountIds { get; set; } = new();
        [Required] public string Currency { get; set; } = null!;
        [Required] public decimal TotalAmount { get; set; }
        [Required] public int AmountOfSplits { get; set; }
        [Required] public FundSplitType SplitType { get; set; }
        public string? Message { get; set; }
        public int? ExpirationHours { get; set; } // Optional: hours until expiration
        [Required] public string PinCode { get; set; } = null!; // Required PIN for fund creation
    }

    [HttpPost("funds")]
    [Authorize]
    public async Task<ActionResult<SnWalletFund>> CreateFund([FromBody] CreateFundRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        // Validate PIN code
        if (!await auth.ValidatePinCode(currentUser.Id, request.PinCode))
            return StatusCode(403, "Invalid PIN Code");

        try
        {
            Duration? expiration = null;
            if (request.ExpirationHours.HasValue)
            {
                expiration = Duration.FromHours(request.ExpirationHours.Value);
            }

            var fund = await payment.CreateFundAsync(
                creatorAccountId: currentUser.Id,
                recipientAccountIds: request.RecipientAccountIds,
                currency: request.Currency,
                totalAmount: request.TotalAmount,
                amountOfSplits: request.AmountOfSplits,
                splitType: request.SplitType,
                message: request.Message,
                expiration: expiration
            );

            return Ok(fund);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpGet("funds")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletFund>>> GetFunds(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] FundStatus? status = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var query = db.WalletFunds
            .Include(f => f.Recipients)
            .ThenInclude(r => r.RecipientAccount)
            .ThenInclude(a => a.Profile)
            .Include(f => f.CreatorAccount)
            .ThenInclude(a => a.Profile)
            .Where(f => f.CreatorAccountId == currentUser.Id ||
                        f.Recipients.Any(r => r.RecipientAccountId == currentUser.Id))
            .AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(f => f.Status == status.Value);
        }

        var fundCount = await query.CountAsync();
        Response.Headers["X-Total"] = fundCount.ToString();

        var funds = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return Ok(funds);
    }

    [HttpGet("funds/{id:guid}")]
    public async Task<ActionResult<SnWalletFund>> GetFund(Guid id)
    {
        var fund = await db.WalletFunds
            .Include(f => f.Recipients)
            .ThenInclude(r => r.RecipientAccount)
            .ThenInclude(a => a.Profile)
            .Include(f => f.CreatorAccount)
            .ThenInclude(a => a.Profile)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (fund is null)
            return NotFound("Fund not found");

        return Ok(fund);
    }

    [HttpPost("funds/{id:guid}/receive")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> ReceiveFund(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var transaction = await payment.ReceiveFundAsync(
                recipientAccountId: currentUser.Id,
                fundId: id
            );

            return Ok(transaction);
        }
        catch (Exception err)
        {
            logger.LogError(err, "Failed to receive fund...");
            return BadRequest(err.Message);
        }
    }

    [HttpGet("overview")]
    [Authorize]
    public async Task<ActionResult<WalletOverview>> GetWalletOverview(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var overview = await payment.GetWalletOverviewAsync(
                accountId: currentUser.Id,
                startDate: startDate,
                endDate: endDate
            );

            return Ok(overview);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }
}
