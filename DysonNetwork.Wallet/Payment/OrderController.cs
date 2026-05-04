using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/orders")]
public class OrderController(
    PaymentService payment,
    AppDatabase db,
    DyCustomAppService.DyCustomAppServiceClient customApps
) : ControllerBase
{
    public class CreateOrderRequest
    {
        public string Currency { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? Remarks { get; set; }
        public string? ProductIdentifier { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public int DurationHours { get; set; } = 24;
        public Guid? PayeeWalletId { get; set; }

        public string ClientId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
    }

    public class AppOrderMetricsRequest
    {
        public string ClientId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    public class AppPayoutRequest
    {
        public string ClientId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
        public Guid PayeeAccountId { get; set; }
        public string Currency { get; set; } = null!;
        public decimal Amount { get; set; }
        public string? Remarks { get; set; }
    }

    public class AppOrderMetricsResponse
    {
        public string AppIdentifier { get; set; } = null!;
        public int TotalOrders { get; set; }
        public int PaidOrders { get; set; }
        public int UnpaidOrders { get; set; }
        public int FinishedOrders { get; set; }
        public int CancelledOrders { get; set; }
        public int ExpiredOrders { get; set; }
        public decimal TotalIncomingAmount { get; set; }
        public decimal PaidIncomingAmount { get; set; }
        public Dictionary<string, decimal> ProductIncomingAmounts { get; set; } = new();
        public Dictionary<string, int> ProductOrderCounts { get; set; } = new();
    }

    private async Task<SnCustomApp?> ValidateClientAsync(string clientId, string clientSecret)
    {
        try
        {
            var clientResp = await customApps.GetCustomAppAsync(new DyGetCustomAppRequest { Slug = clientId });
            if (clientResp.App is null) return null;

            var client = SnCustomApp.FromProtoValue(clientResp.App);
            var secret = await customApps.CheckCustomAppSecretAsync(new DyCheckCustomAppSecretRequest
            {
                AppId = client.Id.ToString(),
                Secret = clientSecret,
            });

            return secret.Valid ? client : null;
        }
        catch (RpcException)
        {
            return null;
        }
    }

    [HttpPost]
    public async Task<ActionResult<SnWalletOrder>> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var client = await ValidateClientAsync(request.ClientId, request.ClientSecret);
        if (client is null) return BadRequest("Invalid client credentials");

        Guid? payeeWalletId = null;
        if (request.PayeeWalletId.HasValue)
        {
            var walletExists = await db.Wallets.AnyAsync(w => w.Id == request.PayeeWalletId.Value);
            if (!walletExists)
                return BadRequest("Payee wallet was not found.");

            payeeWalletId = request.PayeeWalletId.Value;
        }
        else if (client.PaymentWalletId.HasValue)
        {
            var walletExists = await db.Wallets.AnyAsync(w => w.Id == client.PaymentWalletId.Value);
            if (!walletExists)
                return BadRequest("Configured payout wallet was not found.");

            payeeWalletId = client.PaymentWalletId.Value;
        }

        var order = await payment.CreateOrderAsync(
            payeeWalletId,
            request.Currency,
            request.Amount,
            NodaTime.Duration.FromHours(request.DurationHours),
            client.ResourceIdentifier,
            request.ProductIdentifier,
            request.Remarks,
            request.Meta
        );

        return Ok(order);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnWalletOrder>> GetOrderById(Guid id)
    {
        var order = await db.PaymentOrders.FindAsync(id);

        if (order == null)
            return NotFound();

        return Ok(order);
    }

    public class PayOrderRequest
    {
        public string PinCode { get; set; } = string.Empty;
    }

    [HttpPost("{id:guid}/pay")]
    [Authorize]
    public async Task<ActionResult<SnWalletOrder>> PayOrder(Guid id, [FromBody] PayOrderRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            // Get the wallet for the current user
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.AccountId == Guid.Parse(currentUser.Id));
            if (wallet == null)
                return BadRequest("Wallet was not found.");

            // Pay the order
            var paidOrder = await payment.PayOrderAsync(id, wallet);
            return Ok(paidOrder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public class UpdateOrderStatusRequest
    {
        public string ClientId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
        public Shared.Models.OrderStatus Status { get; set; }
    }

    [HttpPatch("{id:guid}/status")]
    public async Task<ActionResult<SnWalletOrder>> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusRequest request)
    {
        var client = await ValidateClientAsync(request.ClientId, request.ClientSecret);
        if (client is null) return BadRequest("Invalid client credentials");

        var order = await db.PaymentOrders.FindAsync(id);

        if (order == null)
            return NotFound();

        if (order.AppIdentifier != client.ResourceIdentifier)
        {
            return BadRequest("Order does not belong to this client.");
        }

        if (request.Status != Shared.Models.OrderStatus.Finished && request.Status != Shared.Models.OrderStatus.Cancelled)
            return BadRequest("Invalid status. Available statuses are Finished, Cancelled.");


        order.Status = request.Status;
        await db.SaveChangesAsync();

        return Ok(order);
    }

    [HttpPost("metrics")]
    public async Task<ActionResult<AppOrderMetricsResponse>> GetAppOrderMetrics([FromBody] AppOrderMetricsRequest request)
    {
        var client = await ValidateClientAsync(request.ClientId, request.ClientSecret);
        if (client is null) return BadRequest("Invalid client credentials");

        var query = db.PaymentOrders
            .Where(o => o.AppIdentifier == client.ResourceIdentifier)
            .AsQueryable();

        if (request.StartDate.HasValue)
        {
            var start = Instant.FromDateTimeUtc(request.StartDate.Value.ToUniversalTime());
            query = query.Where(o => o.CreatedAt >= start);
        }

        if (request.EndDate.HasValue)
        {
            var end = Instant.FromDateTimeUtc(request.EndDate.Value.ToUniversalTime());
            query = query.Where(o => o.CreatedAt <= end);
        }

        var orders = await query.ToListAsync();

        var response = new AppOrderMetricsResponse
        {
            AppIdentifier = client.ResourceIdentifier,
            TotalOrders = orders.Count,
            PaidOrders = orders.Count(o => o.Status == OrderStatus.Paid),
            UnpaidOrders = orders.Count(o => o.Status == OrderStatus.Unpaid),
            FinishedOrders = orders.Count(o => o.Status == OrderStatus.Finished),
            CancelledOrders = orders.Count(o => o.Status == OrderStatus.Cancelled),
            ExpiredOrders = orders.Count(o => o.Status == OrderStatus.Expired),
            TotalIncomingAmount = orders.Sum(o => o.Amount),
            PaidIncomingAmount = orders
                .Where(o => o.Status is OrderStatus.Paid or OrderStatus.Finished)
                .Sum(o => o.Amount),
            ProductIncomingAmounts = orders
                .GroupBy(o => o.ProductIdentifier ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Sum(o => o.Amount)),
            ProductOrderCounts = orders
                .GroupBy(o => o.ProductIdentifier ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return Ok(response);
    }

    [HttpPost("payouts")]
    public async Task<ActionResult<SnWalletTransaction>> CreateAppPayout([FromBody] AppPayoutRequest request)
    {
        var client = await ValidateClientAsync(request.ClientId, request.ClientSecret);
        if (client is null) return BadRequest("Invalid client credentials");

        if (!client.PaymentWalletId.HasValue)
            return BadRequest("This app does not have a configured payout wallet.");

        var payerWallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == client.PaymentWalletId.Value);
        if (payerWallet is null)
            return BadRequest("Configured payout wallet was not found.");

        if (payerWallet.AccountId == request.PayeeAccountId)
            return BadRequest("Cannot pay the payout wallet owner.");

        try
        {
            var payeeWallet = await db.Wallets.FirstOrDefaultAsync(w => w.AccountId == request.PayeeAccountId);
            if (payeeWallet is null)
                return BadRequest("Payee wallet was not found.");

            var transaction = await payment.CreateTransactionAsync(
                payerWalletId: payerWallet.Id,
                payeeWalletId: payeeWallet.Id,
                currency: request.Currency,
                amount: request.Amount,
                remarks: request.Remarks,
                type: TransactionType.Transfer
            );

            return Ok(transaction);
        }
        catch (Exception err)
        {
            return BadRequest(err.Message);
        }
    }
}
