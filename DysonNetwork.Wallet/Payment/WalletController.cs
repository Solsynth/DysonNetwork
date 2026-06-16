using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/wallets")]
public class WalletController(
    AppDatabase db,
    WalletService ws,
    PaymentService payment,
    RemoteAccountService remoteAccounts,
    RemotePublisherService publishers,
    RemoteActionLogService als,
    ICacheService cache,
    ILogger<WalletController> logger
) : ControllerBase
{
    private const string NoPinProvided = "NO_PIN_PROVEDED";

    public class CreateWalletRequest
    {
        public string? Name { get; set; }
        public Guid? RealmId { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnWallet>> CreateWallet([FromBody] CreateWalletRequest? request = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var currentAccountId = Guid.Parse(currentUser.Id);

        if (request?.RealmId.HasValue == true)
        {
            var publisher = (await publishers.ListPublishers(realmId: request.RealmId.Value.ToString())).FirstOrDefault();
            if (publisher is null)
                return NotFound("Realm publisher was not found.");
            if (!await publishers.IsMemberWithRole(publisher.Id, currentAccountId, PublisherMemberRole.Editor))
                return StatusCode(403, "You must be an editor of the realm publisher to create a realm wallet.");

                var realmWallet = await ws.CreateWalletAsync(realmId: request.RealmId.Value, name: request.Name);
                return Ok(realmWallet);
            }

            var wallet = await ws.CreateWalletAsync(accountId: currentAccountId, name: request?.Name);

            als.CreateActionLog(
                currentAccountId,
                "wallets.create",
                new Dictionary<string, object>
                {
                    { "wallet_id", wallet.Id.ToString() }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );
            
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var wallet = await ws.GetAccountWalletAsync(Guid.Parse(currentUser.Id));
        if (wallet is null) return NotFound("Wallet was not found, please create one first.");
        return Ok(wallet);
    }

    [HttpGet("all")]
    [Authorize]
    public async Task<ActionResult<List<SnWallet>>> GetWallets()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentAccountId = Guid.Parse(currentUser.Id);
        var wallets = await ws.GetAccountWalletsAsync(currentAccountId);

        // Also include realm wallets where user is a member
        var memberPublishers = await publishers.GetUserPublishers(currentAccountId);
        var seenRealmIds = new HashSet<Guid>();
        foreach (var publisher in memberPublishers)
        {
            if (publisher.RealmId.HasValue && seenRealmIds.Add(publisher.RealmId.Value))
            {
                var realmWallets = await ws.GetRealmWalletsAsync(publisher.RealmId.Value);
                wallets.AddRange(realmWallets);
            }
        }

        return Ok(wallets);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnWallet>> GetWalletById(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var wallet = await ws.GetWalletAsync(id);
        if (wallet is null) return NotFound();

        if (wallet.AccountId == Guid.Parse(currentUser.Id))
            return Ok(wallet);

        if (wallet.RealmId.HasValue)
        {
            var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
            if (publisher is null)
                return NotFound("Realm publisher was not found.");
            if (await publishers.IsMemberWithRole(publisher.Id, Guid.Parse(currentUser.Id), PublisherMemberRole.Viewer))
                return Ok(wallet);
        }

        return StatusCode(403);
    }

    public class WalletStats
    {
        public Instant PeriodBegin { get; set; }
        public Instant PeriodEnd { get; set; }
        public int TotalTransactions { get; set; }
        public int TotalOrders { get; set; }
        public Dictionary<string, decimal> IncomeCategories { get; set; } = null!;
        public Dictionary<string, decimal> OutgoingCategories { get; set; } = null!;
        public decimal TotalIncome => IncomeCategories.Values.Sum();
        public decimal TotalOutgoing => OutgoingCategories.Values.Sum();
        public decimal Sum => TotalIncome - TotalOutgoing;
    }

    [HttpGet("stats")]
    [Authorize]
    public async Task<ActionResult<WalletStats>> GetWalletStats(
        [FromQuery] int period = 30,
        [FromQuery] List<Guid>? wallets = null,
        [FromQuery] List<string>? currencies = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentAccountId = Guid.Parse(currentUser.Id);
        var resolvedWallets = await ResolveAccessibleWalletsAsync(currentAccountId, wallets);
        if (resolvedWallets is null || resolvedWallets.Count == 0)
            return NotFound("Wallet was not found, please create one first.");

        var resolvedWalletIds = resolvedWallets.Select(w => w.Id).ToHashSet();
        var currencyFilters = NormalizeFilters(currencies, "points");

        var periodEnd = SystemClock.Instance.GetCurrentInstant();
        var periodBegin = periodEnd.Minus(Duration.FromDays(period));

        var cacheKey = $"wallet:stats:{currentAccountId}:{period}:{string.Join(',', resolvedWalletIds.OrderBy(id => id))}:{string.Join(',', currencyFilters.OrderBy(c => c))}";
        var cached = await cache.GetAsync<WalletStats>(cacheKey);
        if (cached != null)
        {
            return Ok(cached);
        }

        var transactions = await db.PaymentTransactions
            .Where(t => (t.PayerWalletId.HasValue && resolvedWalletIds.Contains(t.PayerWalletId.Value) ||
                         t.PayeeWalletId.HasValue && resolvedWalletIds.Contains(t.PayeeWalletId.Value)) &&
                        currencyFilters.Contains(t.Currency) &&
                        t.CreatedAt >= periodBegin && t.CreatedAt <= periodEnd)
            .ToListAsync();

        var orders = await db.PaymentOrders
            .Where(o => o.PayeeWalletId.HasValue && resolvedWalletIds.Contains(o.PayeeWalletId.Value) &&
                        currencyFilters.Contains(o.Currency) &&
                        o.CreatedAt >= periodBegin && o.CreatedAt <= periodEnd)
            .ToListAsync();

        var incomeCategories = transactions
            .Where(t => t.PayeeWalletId.HasValue && resolvedWalletIds.Contains(t.PayeeWalletId.Value) && currencyFilters.Contains(t.Currency))
            .GroupBy(t => t.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var outgoingCategories = transactions
            .Where(t => t.PayerWalletId.HasValue && resolvedWalletIds.Contains(t.PayerWalletId.Value) && currencyFilters.Contains(t.Currency))
            .GroupBy(t => t.Type.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var stats = new WalletStats
        {
            PeriodBegin = periodBegin,
            PeriodEnd = periodEnd,
            TotalTransactions = transactions.Count,
            TotalOrders = orders.Count,
            IncomeCategories = incomeCategories,
            OutgoingCategories = outgoingCategories
        };

        await cache.SetAsync(cacheKey, stats, TimeSpan.FromHours(1));
        return Ok(stats);
    }

    private async Task<List<SnWallet>?> ResolveAccessibleWalletsAsync(Guid currentAccountId, List<Guid>? walletIds)
    {
        if (walletIds is null || walletIds.Count == 0)
        {
            var wallet = await ws.GetAccountWalletAsync(currentAccountId);
            return wallet is null ? null : new List<SnWallet> { wallet };
        }

        var wallets = new List<SnWallet>();
        foreach (var walletId in walletIds.Distinct())
        {
            var wallet = await ws.GetWalletAsync(walletId);
            if (wallet is null)
                return null;

            if (wallet.AccountId == currentAccountId)
            {
                wallets.Add(wallet);
                continue;
            }

            if (!wallet.RealmId.HasValue)
                return null;

            var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
            if (publisher is null)
                return null;

            if (!await publishers.IsMemberWithRole(publisher.Id, currentAccountId, PublisherMemberRole.Viewer))
                return null;

            wallets.Add(wallet);
        }

        return wallets;
    }

    private static HashSet<string> NormalizeFilters(IEnumerable<string>? values, string defaultValue)
    {
        var filters = values?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (filters.Count == 0)
            filters.Add(defaultValue);

        return filters;
    }

    [HttpGet("transactions")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletTransaction>>> GetTransactions(
        [FromQuery] Guid? wallet = null,
        [FromQuery] string? direction = null,
        [FromQuery] TransactionType? type = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentAccountId = Guid.Parse(currentUser.Id);
        var targetWallet = await ResolveAccessibleWalletAsync(currentAccountId, wallet);
        if (targetWallet is null) return NotFound();

        var query = db.PaymentTransactions
            .Where(t => t.PayeeWalletId == targetWallet.Id || t.PayerWalletId == targetWallet.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(direction))
        {
            switch (direction.Trim().ToLowerInvariant())
            {
                case "income":
                case "incoming":
                case "in":
                    query = query.Where(t => t.PayeeWalletId == targetWallet.Id);
                    break;
                case "outcome":
                case "outgoing":
                case "out":
                    query = query.Where(t => t.PayerWalletId == targetWallet.Id);
                    break;
                default:
                    return BadRequest("Invalid direction. Use income or outcome.");
            }
        }

        if (type.HasValue)
            query = query.Where(t => t.Type == type.Value);

        query = query.OrderByDescending(t => t.CreatedAt);

        var transactionCount = await query.CountAsync();
        Response.Headers["X-Total"] = transactionCount.ToString();

        var transactions = await query
            .Skip(offset)
            .Take(take)
            .Include(t => t.PayerWallet)
            .Include(t => t.PayeeWallet)
            .ToListAsync();

        var accountsRequiresLoad = transactions
            .SelectMany(t => new[] { t.PayerWallet?.AccountId, t.PayeeWallet?.AccountId })
            .Where(w => w != null)
            .Select(w => w!.Value)
            .Distinct()
            .ToList();
        var accounts = await remoteAccounts.GetAccountBatch(accountsRequiresLoad);

        // Assign account data back to wallet properties
        foreach (var transaction in transactions)
        {
            if (transaction.PayerWallet?.AccountId != null)
            {
                var accountId = transaction.PayerWallet.AccountId;
                var account = accounts.FirstOrDefault(a => a.Id == accountId.ToString());
                if (account != null)
                    transaction.PayerWallet.Account = SnAccount.FromProtoValue(account);
            }

            if (transaction.PayeeWallet?.AccountId != null)
            {
                var accountId = transaction.PayeeWallet.AccountId;
                var account = accounts.FirstOrDefault(a => a.Id == accountId.ToString());
                if (account != null)
                    transaction.PayeeWallet.Account = SnAccount.FromProtoValue(account);
            }
        }

        return Ok(transactions);
    }

    [HttpGet("transactions/{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> GetTransactionById(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentAccountId = Guid.Parse(currentUser.Id);
        var transaction = await db.PaymentTransactions
            .Include(t => t.PayerWallet)
            .Include(t => t.PayeeWallet)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transaction is null)
            return NotFound();

        var accessibleWalletIds = new List<Guid>();
        if (transaction.PayerWalletId.HasValue)
        {
            var payerWallet = await ResolveAccessibleWalletAsync(currentAccountId, transaction.PayerWalletId.Value);
            if (payerWallet is not null)
                accessibleWalletIds.Add(payerWallet.Id);
        }

        if (transaction.PayeeWalletId.HasValue)
        {
            var payeeWallet = await ResolveAccessibleWalletAsync(currentAccountId, transaction.PayeeWalletId.Value);
            if (payeeWallet is not null)
                accessibleWalletIds.Add(payeeWallet.Id);
        }

        if (!accessibleWalletIds.Contains(transaction.PayerWalletId ?? Guid.Empty) &&
            !accessibleWalletIds.Contains(transaction.PayeeWalletId ?? Guid.Empty))
            return NotFound();

        var accountIds = new[] { transaction.PayerWallet?.AccountId, transaction.PayeeWallet?.AccountId }
            .Where(accountId => accountId.HasValue)
            .Select(accountId => accountId!.Value)
            .Distinct()
            .ToList();

        var accounts = accountIds.Count > 0
            ? await remoteAccounts.GetAccountBatch(accountIds)
            : [];

        if (transaction.PayerWallet?.AccountId != null)
        {
            var account = accounts.FirstOrDefault(a => a.Id == transaction.PayerWallet.AccountId.ToString());
            if (account != null)
                transaction.PayerWallet.Account = SnAccount.FromProtoValue(account);
        }

        if (transaction.PayeeWallet?.AccountId != null)
        {
            var account = accounts.FirstOrDefault(a => a.Id == transaction.PayeeWallet.AccountId.ToString());
            if (account != null)
                transaction.PayeeWallet.Account = SnAccount.FromProtoValue(account);
        }

        return Ok(transaction);
    }

    [HttpGet("orders")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletOrder>>> GetOrders(
        [FromQuery] Guid? wallet = null,
        [FromQuery] string? direction = null,
        [FromQuery] TransactionType? type = null,
        [FromQuery] OrderStatus? status = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentAccountId = Guid.Parse(currentUser.Id);
        var targetWallet = await ResolveAccessibleWalletAsync(currentAccountId, wallet);
        if (targetWallet is null) return NotFound();

        var query = db.PaymentOrders.AsQueryable()
            .Include(o => o.Transaction)
            .Where(o => o.Transaction != null && (o.Transaction.PayeeWalletId == targetWallet.Id ||
                                                  o.Transaction.PayerWalletId == targetWallet.Id))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(direction))
        {
            switch (direction.Trim().ToLowerInvariant())
            {
                case "income":
                case "incoming":
                case "in":
                    query = query.Where(o => o.Transaction != null && o.Transaction.PayeeWalletId == targetWallet.Id);
                    break;
                case "outcome":
                case "outgoing":
                case "out":
                    query = query.Where(o => o.Transaction != null && o.Transaction.PayerWalletId == targetWallet.Id);
                    break;
                default:
                    return BadRequest("Invalid direction. Use income or outcome.");
            }
        }

        if (type.HasValue)
            query = query.Where(o => o.Transaction != null && o.Transaction.Type == type.Value);

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        var orderCount = await query.CountAsync();
        Response.Headers["X-Total"] = orderCount.ToString();

        var orders = await query
            .Skip(offset)
            .Take(take)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return Ok(orders);
    }

    private async Task<SnWallet?> ResolveAccessibleWalletAsync(Guid currentAccountId, Guid? walletId)
    {
        if (!walletId.HasValue)
            return await ws.GetAccountWalletAsync(currentAccountId);

        var wallet = await ws.GetWalletAsync(walletId.Value);
        if (wallet is null)
            return null;

        if (wallet.AccountId == currentAccountId)
            return wallet;

        if (!wallet.RealmId.HasValue)
            return null;

        var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
        if (publisher is null)
            return null;

        return await publishers.IsMemberWithRole(publisher.Id, currentAccountId, PublisherMemberRole.Viewer)
            ? wallet
            : null;
    }

    public class WalletBalanceRequest
    {
        public string? Remark { get; set; }
        [Required] public decimal Amount { get; set; }
        [Required] public string Currency { get; set; } = null!;

        public Guid? AccountId { get; set; }
        public Guid? WalletId { get; set; }

        /// <summary>
        /// When force operation is enabled, the wallet will ignore insufficent balance
        /// </summary>
        public bool ForceOperation { get; set; } = false;
    }

    public class WalletTransferRequest
    {
        public string? Remark { get; set; }
        [Required] public decimal Amount { get; set; }
        [Required] public string Currency { get; set; } = null!;
        public Guid? PayerWalletId { get; set; }
        public Guid? PayeeAccountId { get; set; }
        public Guid? PayeeWalletId { get; set; }
        public string? PayeePublicId { get; set; }
        public string? PinCode { get; set; }

        // Transaction lifecycle options
        public bool Freeze { get; set; } = false;
        public bool RequireConfirmation { get; set; } = false;
        public Guid? TransferRequestId { get; set; }
    }

    public class CreateWalletTransferRequestRequest
    {
        [Required] public decimal Amount { get; set; }
        [Required] public string Currency { get; set; } = null!;
        public string? Remark { get; set; }
        public Guid? WalletId { get; set; }
        public int ExpirationHours { get; set; } = 24;
        public bool Freeze { get; set; } = false;
        public bool RequireConfirmation { get; set; } = false;
    }

    public class WalletTransferRequestResponse
    {
        public Guid Id { get; set; }
        public WalletTransferRequestStatus Status { get; set; }
        public string Currency { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? Remark { get; set; }
        public bool Freeze { get; set; }
        public bool RequireConfirmation { get; set; }
        public Instant ExpiresAt { get; set; }
        public Instant? FulfilledAt { get; set; }
        public Guid PayeeWalletId { get; set; }
        public string? PayeePublicId { get; set; }
        public Guid? TransactionId { get; set; }
    }

    private static WalletTransferRequestResponse MapTransferRequest(SnWalletTransferRequest request)
    {
        return new WalletTransferRequestResponse
        {
            Id = request.Id,
            Status = request.Status,
            Currency = request.Currency,
            Amount = request.Amount,
            Remark = request.Remark,
            Freeze = request.Freeze,
            RequireConfirmation = request.RequireConfirmation,
            ExpiresAt = request.ExpiresAt,
            FulfilledAt = request.FulfilledAt,
            PayeeWalletId = request.PayeeWalletId,
            PayeePublicId = request.PayeeWallet?.PublicId,
            TransactionId = request.TransactionId,
        };
    }

    [HttpPost("balance")]
    [Authorize]
    [AskPermission("wallets.balance.modify")]
    public async Task<ActionResult<SnWalletTransaction>> ModifyWalletBalance([FromBody] WalletBalanceRequest request)
    {
        if (request.WalletId is null && request.AccountId is null)
            return BadRequest("You must specify either WalletId or AccountId.");

        var wallet = request.AccountId is not null
            ? await ws.GetAccountWalletAsync(request.AccountId.Value)
            : await ws.GetWalletAsync(request.WalletId!.Value);
        if (wallet is null) return NotFound("Wallet was not found.");

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

    [HttpPost("transfer")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> Transfer([FromBody] WalletTransferRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentAccountId = Guid.Parse(currentUser.Id);
        var pinCode = string.IsNullOrWhiteSpace(request.PinCode) ? NoPinProvided : request.PinCode;

        try
        {
            SnWallet payerWallet;
            if (request.PayerWalletId.HasValue)
            {
                payerWallet = await ws.GetWalletAsync(request.PayerWalletId.Value) ?? throw new InvalidOperationException("Payer wallet not found.");

                if (payerWallet.AccountId == currentAccountId)
                {
                }
                else if (payerWallet.RealmId.HasValue)
                {
                    var publisher = (await publishers.ListPublishers(realmId: payerWallet.RealmId.Value.ToString())).FirstOrDefault();
                    if (publisher is null)
                        return NotFound("Realm publisher was not found.");
                    if (!await publishers.IsMemberWithRole(publisher.Id, currentAccountId, PublisherMemberRole.Editor))
                        return StatusCode(403, "You must be an editor of the realm publisher to spend from this wallet.");
                }
                else
                {
                    return StatusCode(403);
                }
            }
            else
            {
                payerWallet = await ws.GetAccountWalletAsync(currentAccountId) ?? throw new InvalidOperationException("Default wallet not found.");
            }

            SnWallet payeeWallet;
            decimal transferAmount = request.Amount;
            string transferCurrency = request.Currency;
            string? transferRemark = request.Remark;
            bool freezeTransfer = request.Freeze;
            bool requireConfirmation = request.RequireConfirmation;
            SnWalletTransferRequest? transferRequest = null;

            if (request.TransferRequestId.HasValue)
            {
                transferRequest = await payment.GetTransferRequestAsync(request.TransferRequestId.Value)
                    ?? throw new InvalidOperationException("Transfer request not found.");
                if (transferRequest.Status != WalletTransferRequestStatus.Active)
                    throw new InvalidOperationException("Transfer request is no longer active.");
                if (transferRequest.ExpiresAt < SystemClock.Instance.GetCurrentInstant())
                    throw new InvalidOperationException("Transfer request has expired.");

                payeeWallet = await ws.GetWalletAsync(transferRequest.PayeeWalletId)
                    ?? throw new InvalidOperationException("Payee wallet not found.");
                transferAmount = transferRequest.Amount;
                transferCurrency = transferRequest.Currency;
                transferRemark = transferRequest.Remark;
                freezeTransfer = transferRequest.Freeze;
                requireConfirmation = transferRequest.RequireConfirmation;
            }
            else if (request.PayeeWalletId.HasValue)
            {
                payeeWallet = await ws.GetWalletAsync(request.PayeeWalletId.Value) ?? throw new InvalidOperationException("Payee wallet not found.");
            }
            else if (!string.IsNullOrWhiteSpace(request.PayeePublicId))
            {
                payeeWallet = await ws.GetWalletByPublicIdAsync(request.PayeePublicId) ?? throw new InvalidOperationException("Payee wallet not found.");
            }
            else if (request.PayeeAccountId.HasValue)
            {
                if (currentAccountId == request.PayeeAccountId.Value)
                    return BadRequest("Cannot transfer to yourself.");

                payeeWallet = await ws.GetAccountWalletAsync(request.PayeeAccountId.Value) ?? throw new InvalidOperationException("Payee wallet not found.");
            }
            else
            {
                return BadRequest("You must specify payee_account_id, payee_wallet_id, or payee_public_id.");
            }

            if (payerWallet.Id == payeeWallet.Id)
                return BadRequest("Cannot transfer to the same wallet.");

            var transaction = await payment.TransferBetweenWalletsAsync(
                payerWallet.Id,
                payeeWallet.Id,
                transferCurrency,
                transferAmount,
                transferRemark,
                freeze: freezeTransfer,
                requireConfirmation: requireConfirmation
            );

            if (transferRequest is not null)
                await payment.FulfillTransferRequestAsync(transferRequest.Id, transaction.Id);

            return Ok(transaction);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPost("transfer/requests")]
    [Authorize]
    public async Task<ActionResult<WalletTransferRequestResponse>> CreateTransferRequest(
        [FromBody] CreateWalletTransferRequestRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            if (request.ExpirationHours <= 0 || request.ExpirationHours > 24 * 7)
                return BadRequest("ExpirationHours must be between 1 and 168.");

            var currentAccountId = Guid.Parse(currentUser.Id);
            var wallet = await ResolveAccessibleWalletAsync(currentAccountId, request.WalletId);
            if (wallet is null)
                return NotFound("Wallet not found.");

            if (wallet.AccountId == currentAccountId)
            {
            }
            else if (wallet.RealmId.HasValue)
            {
                var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
                if (publisher is null)
                    return NotFound("Realm publisher was not found.");
                if (!await publishers.IsMemberWithRole(publisher.Id, currentAccountId, PublisherMemberRole.Editor))
                    return StatusCode(403, "You must be an editor of the realm publisher to create transfer requests for this wallet.");
            }

            if (string.IsNullOrWhiteSpace(wallet.PublicId))
                return BadRequest("Wallet public ID is not enabled.");

            var transferRequest = await payment.CreateTransferRequestAsync(
                currentAccountId,
                wallet.Id,
                request.Currency,
                request.Amount,
                request.Remark,
                Duration.FromHours(request.ExpirationHours),
                request.Freeze,
                request.RequireConfirmation
            );
            transferRequest.PayeeWallet = wallet;

            return Ok(MapTransferRequest(transferRequest));
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpGet("transfer/requests/{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<WalletTransferRequestResponse>> GetTransferRequest(Guid id)
    {
        var request = await payment.GetTransferRequestAsync(id);
        if (request is null) return NotFound();
        if (request.Status != WalletTransferRequestStatus.Active) return NotFound();
        if (request.ExpiresAt < SystemClock.Instance.GetCurrentInstant()) return NotFound();

        return Ok(MapTransferRequest(request));
    }

    [HttpPost("{id:guid}/default")]
    [Authorize]
    public async Task<ActionResult<SnWallet>> SetDefaultWallet(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var wallet = await ws.GetWalletAsync(id);
        if (wallet is null) return NotFound();
        if (wallet.AccountId != Guid.Parse(currentUser.Id)) return StatusCode(403);

        return Ok(await ws.SetPrimaryWalletAsync(id));
    }

    [HttpPost("{id:guid}/public-id/enable")]
    [Authorize]
    public async Task<ActionResult<SnWallet>> EnablePublicId(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var wallet = await ws.GetWalletAsync(id);
        if (wallet is null) return NotFound();

        if (wallet.AccountId == Guid.Parse(currentUser.Id))
            return Ok(await ws.EnablePublicIdAsync(id));

        if (wallet.RealmId.HasValue)
        {
            var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
            if (publisher is null)
                return NotFound("Realm publisher was not found.");
            if (await publishers.IsMemberWithRole(publisher.Id, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
                return Ok(await ws.EnablePublicIdAsync(id));
        }

        return StatusCode(403);
    }

    [HttpPost("{id:guid}/public-id/disable")]
    [Authorize]
    public async Task<ActionResult<SnWallet>> DisablePublicId(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var wallet = await ws.GetWalletAsync(id);
        if (wallet is null) return NotFound();

        if (wallet.AccountId == Guid.Parse(currentUser.Id))
            return Ok(await ws.DisablePublicIdAsync(id));

        if (wallet.RealmId.HasValue)
        {
            var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
            if (publisher is null)
                return NotFound("Realm publisher was not found.");
            if (await publishers.IsMemberWithRole(publisher.Id, Guid.Parse(currentUser.Id), PublisherMemberRole.Editor))
                return Ok(await ws.DisablePublicIdAsync(id));
        }

        return StatusCode(403);
    }

    public class CreateFundRequest
    {
        [Required] public List<Guid> RecipientAccountIds { get; set; } = new();
        [Required] public string Currency { get; set; } = null!;
        [Required] public decimal TotalAmount { get; set; }
        [Required] public int AmountOfSplits { get; set; }
        [Required] public Shared.Models.FundSplitType SplitType { get; set; }
        public string? Message { get; set; }
        public int? ExpirationHours { get; set; } // Optional: hours until expiration
        public string? PinCode { get; set; }

        // Raising mode options
        public bool IsRaising { get; set; } = false;
        public decimal TargetAmount { get; set; } = 0;
        public Shared.Models.ContributionType ContributionType { get; set; } = Shared.Models.ContributionType.Free;
        public decimal ContributionAmount { get; set; } = 0;
        public bool IsOpen { get; set; } = true;
        public Instant? DeadlineAt { get; set; }
    }

    [HttpPost("funds")]
    [Authorize]
    public async Task<ActionResult<SnWalletFund>> CreateFund([FromBody] CreateFundRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var pinCode = string.IsNullOrWhiteSpace(request.PinCode) ? NoPinProvided : request.PinCode;
            Duration? expiration = null;
            if (request.ExpirationHours.HasValue)
            {
                expiration = Duration.FromHours(request.ExpirationHours.Value);
            }

            SnWalletFund fund;

            if (request.IsRaising)
            {
                fund = await payment.CreateRaisingFundAsync(
                    creatorAccountId: Guid.Parse(currentUser.Id),
                    currency: request.Currency,
                    targetAmount: request.TargetAmount,
                    maxParticipants: request.AmountOfSplits,
                    contributionType: request.ContributionType,
                    contributionAmount: request.ContributionAmount,
                    isOpen: request.IsOpen,
                    invitedAccountIds: request.RecipientAccountIds,
                    message: request.Message,
                    expiration: expiration,
                    deadlineAt: request.DeadlineAt
                );
            }
            else
            {
                fund = await payment.CreateFundAsync(
                    creatorAccountId: Guid.Parse(currentUser.Id),
                    recipientAccountIds: request.RecipientAccountIds,
                    currency: request.Currency,
                    totalAmount: request.TotalAmount,
                    amountOfSplits: request.AmountOfSplits,
                    splitType: request.SplitType,
                    message: request.Message,
                    expiration: expiration
                );
            }

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
        [FromQuery] Shared.Models.FundStatus? status = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentUserId = Guid.Parse(currentUser.Id);
        var query = db.WalletFunds
            .Include(f => f.Recipients)
            .Where(f => f.CreatorAccountId == currentUserId)
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
            .FirstOrDefaultAsync(f => f.Id == id);

        if (fund is null)
            return NotFound("Fund not found");

        // Load recipient account data
        var recipientAccountIds = fund.Recipients
            .Select(r => r.RecipientAccountId)
            .Distinct()
            .ToList();

        var accounts = await remoteAccounts.GetAccountBatch(recipientAccountIds);

        // Assign account data to recipients
        foreach (var recipient in fund.Recipients)
        {
            var account = accounts.FirstOrDefault(a => a.Id == recipient.RecipientAccountId.ToString());
            if (account != null)
                recipient.RecipientAccount = SnAccount.FromProtoValue(account);
        }

        return Ok(fund);
    }

    [HttpPost("funds/{id:guid}/receive")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> ReceiveFund(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var walletTransaction = await payment.ReceiveFundAsync(
                recipientAccountId: Guid.Parse(currentUser.Id),
                fundId: id
            );

            return Ok(walletTransaction);
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var overview = await payment.GetWalletOverviewAsync(
                accountId: Guid.Parse(currentUser.Id),
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

    // --- Transaction Lifecycle Endpoints ---

    [HttpPost("transactions/{id:guid}/confirm")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> ConfirmTransaction(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var transaction = await payment.ConfirmTransactionAsync(id, Guid.Parse(currentUser.Id));
            return Ok(transaction);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpPost("transactions/{id:guid}/reject")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> RejectTransaction(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var transaction = await payment.RejectTransactionAsync(id, Guid.Parse(currentUser.Id));
            return Ok(transaction);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }

    [HttpGet("transactions/pending")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletTransaction>>> GetPendingTransactions(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var currentAccountId = Guid.Parse(currentUser.Id);

        // Get user's wallet
        var wallet = await ws.GetAccountWalletAsync(currentAccountId);
        if (wallet is null) return NotFound("Wallet not found");

        // Find pending/frozen transactions where user is the payee
        var query = db.PaymentTransactions
            .Where(t => t.PayeeWalletId == wallet.Id &&
                        (t.Status == Shared.Models.TransactionStatus.Pending ||
                         t.Status == Shared.Models.TransactionStatus.Frozen) &&
                        t.RequireConfirmation)
            .OrderByDescending(t => t.CreatedAt);

        var totalCount = await query.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var transactions = await query
            .Skip(offset)
            .Take(take)
            .Include(t => t.PayerWallet)
            .ToListAsync();

        // Load payer account info
        var payerAccountIds = transactions
            .Where(t => t.PayerWallet?.AccountId != null)
            .Select(t => t.PayerWallet!.AccountId!.Value)
            .Distinct()
            .ToList();

        var accounts = await remoteAccounts.GetAccountBatch(payerAccountIds);

        foreach (var transaction in transactions)
        {
            if (transaction.PayerWallet?.AccountId != null)
            {
                var account = accounts.FirstOrDefault(a => a.Id == transaction.PayerWallet.AccountId.ToString());
                if (account != null)
                    transaction.PayerWallet.Account = SnAccount.FromProtoValue(account);
            }
        }

        return Ok(transactions);
    }

    // --- Fund Raising Endpoints ---

    public class ContributeFundRequest
    {
        public decimal Amount { get; set; } = 0; // For Free contribution type
    }

    [HttpPost("funds/{id:guid}/contribute")]
    [Authorize]
    public async Task<ActionResult<SnWalletTransaction>> ContributeToFund(Guid id, [FromBody] ContributeFundRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var transaction = await payment.ContributeToFundAsync(
                contributorAccountId: Guid.Parse(currentUser.Id),
                fundId: id,
                amount: request.Amount
            );

            return Ok(transaction);
        }
        catch (Exception err)
        {
            logger.LogError(err, "Failed to contribute to fund...");
            return BadRequest(err.Message);
        }
    }

    [HttpGet("funds/{id:guid}/contributors")]
    public async Task<ActionResult<List<SnWalletFundRecipient>>> GetFundContributors(Guid id)
    {
        var fund = await db.WalletFunds
            .Include(f => f.Recipients)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (fund is null)
            return NotFound("Fund not found");

        if (!fund.IsRaising)
            return BadRequest("This fund is not in raising mode");

        // Load contributor account data
        var contributorAccountIds = fund.Recipients
            .Where(r => r.IsReceived)
            .Select(r => r.RecipientAccountId)
            .Distinct()
            .ToList();

        var accounts = await remoteAccounts.GetAccountBatch(contributorAccountIds);

        // Assign account data to contributors
        foreach (var contributor in fund.Recipients.Where(r => r.IsReceived))
        {
            var account = accounts.FirstOrDefault(a => a.Id == contributor.RecipientAccountId.ToString());
            if (account != null)
                contributor.RecipientAccount = SnAccount.FromProtoValue(account);
        }

        return Ok(fund.Recipients.Where(r => r.IsReceived).ToList());
    }
}
