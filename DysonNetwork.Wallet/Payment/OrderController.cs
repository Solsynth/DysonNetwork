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
    DyCustomAppService.DyCustomAppServiceClient customApps,
    WalletService ws,
    RemotePublisherService publishers
) : ControllerBase
{
    private const string NoPinProvided = "NO_PIN_PROVEDED";

    public class CreateOrderRequest
    {
        // Legacy: used when Items is null
        public string? Currency { get; set; }
        public decimal? Amount { get; set; }
        public string? Remarks { get; set; }
        public string? ProductIdentifier { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public int DurationHours { get; set; } = 24;
        public Guid? PayeeWalletId { get; set; }

        // New: order line items referencing app products
        public List<OrderItemRequest>? Items { get; set; }

        public string ClientId { get; set; } = null!;
        public string ClientSecret { get; set; } = null!;
    }

    public class OrderItemRequest
    {
        public string ProductIdentifier { get; set; } = null!;
        public int Quantity { get; set; } = 1;
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
        else
        {
            payeeWalletId = await ResolveMerchantWalletIdAsync(client);
            if (!payeeWalletId.HasValue)
                return BadRequest("Merchant payout wallet was not configured.");
        }

        // New: order with product line items
        if (request.Items is { Count: > 0 })
        {
            var items = new List<SnWalletOrderItem>();
            decimal totalAmount = 0;
            string? currency = null;

            foreach (var item in request.Items)
            {
                // Validate product exists for this app via gRPC
                var productResp = await customApps.GetAppProductAsync(new DyGetAppProductRequest
                {
                    AppId = client.Id.ToString(),
                    Identifier = item.ProductIdentifier
                });
                if (productResp.Product is null)
                    return BadRequest($"Product '{item.ProductIdentifier}' not found for this app.");

                var product = SnAppProduct.FromProto(productResp.Product);

                if (currency is null)
                    currency = product.Currency;
                else if (currency != product.Currency)
                    return BadRequest("All items must use the same currency.");

                var unitPrice = product.Price;
                var lineTotal = unitPrice * item.Quantity;

                items.Add(new SnWalletOrderItem
                {
                    ProductIdentifier = item.ProductIdentifier,
                    Quantity = item.Quantity,
                    UnitPrice = unitPrice,
                    Currency = product.Currency
                });

                totalAmount += lineTotal;
            }

            // Build subscription meta for recurring products
            var meta = request.Meta ?? new Dictionary<string, object>();
            var subscriptionItems = items
                .Select(i => new { i.ProductIdentifier, i.Quantity })
                .ToList();
            // Resolve subscription products by checking the first item's product (ponytail: single-currency, single-recurrence per order)
            var firstProduct = SnAppProduct.FromProto((await customApps.GetAppProductAsync(new DyGetAppProductRequest
            {
                AppId = client.Id.ToString(),
                Identifier = request.Items[0].ProductIdentifier
            })).Product!);
            if (firstProduct.Recurrence != ProductRecurrence.None)
            {
                var cycleDays = firstProduct.Recurrence switch
                {
                    ProductRecurrence.Weekly => 7,
                    ProductRecurrence.Monthly => 30,
                    ProductRecurrence.Yearly => 365,
                    _ => 30
                };
                meta["app_subscription"] = new Dictionary<string, object>
                {
                    ["identifier"] = firstProduct.Identifier,
                    ["display_name"] = firstProduct.DisplayName,
                    ["currency"] = firstProduct.Currency,
                    ["base_price"] = firstProduct.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["group_identifier"] = firstProduct.GroupIdentifier ?? string.Empty,
                    ["cycle_duration_days"] = cycleDays,
                    ["app_identifier"] = client.ResourceIdentifier,
                    ["payment_method"] = SubscriptionPaymentMethod.InAppWallet
                };
            }

            var order = await payment.CreateOrderAsync(
                payeeWalletId,
                currency!,
                totalAmount,
                NodaTime.Duration.FromHours(request.DurationHours),
                client.ResourceIdentifier,
                productIdentifier: null,
                request.Remarks,
                meta,
                items: items
            );

            return Ok(order);
        }

        // Legacy: order without line items
        var legacyOrder = await payment.CreateOrderAsync(
            payeeWalletId,
            request.Currency!,
            request.Amount!.Value,
            NodaTime.Duration.FromHours(request.DurationHours),
            client.ResourceIdentifier,
            request.ProductIdentifier,
            request.Remarks,
            request.Meta
        );

        return Ok(legacyOrder);
    }

    public class PaymentOrderResponse
    {
        public Guid Id { get; set; }
        public OrderStatus Status { get; set; }
        public string Currency { get; set; } = null!;
        public string? Remarks { get; set; }
        public string? AppIdentifier { get; set; }
        public string? ProductIdentifier { get; set; }
        public Dictionary<string, object>? Meta { get; set; }
        public decimal Amount { get; set; }
        public Instant CreatedAt { get; set; }
        public Instant UpdatedAt { get; set; }
        public DateTimeOffset ExpiredAt { get; set; }
        public Guid? PayeeWalletId { get; set; }
        public Guid? TransactionId { get; set; }
        public List<SnWalletOrderItem> Items { get; set; } = new();

        public DyCustomApp? App { get; set; }
        public DyDeveloper? Developer { get; set; }

        public static PaymentOrderResponse FromOrder(SnWalletOrder order, DyCustomApp? app, DyDeveloper? developer)
        {
            return new PaymentOrderResponse
            {
                Id = order.Id,
                Status = order.Status,
                Currency = order.Currency,
                Remarks = order.Remarks,
                AppIdentifier = order.AppIdentifier,
                ProductIdentifier = order.ProductIdentifier,
                Meta = order.Meta,
                Amount = order.Amount,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
                ExpiredAt = new DateTimeOffset(order.ExpiredAt.ToDateTimeUtc(), TimeSpan.Zero),
                PayeeWalletId = order.PayeeWalletId,
                TransactionId = order.TransactionId,
                Items = order.Items,
                App = app,
                Developer = developer
            };
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentOrderResponse>> GetOrderById(Guid id)
    {
        var order = await db.PaymentOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound();

        DyCustomApp? app = null;
        DyDeveloper? developer = null;
        if (!string.IsNullOrWhiteSpace(order.AppIdentifier) && Guid.TryParse(order.AppIdentifier.Replace("developer.app:", ""), out var appId))
        {
            try
            {
                var devResp = await customApps.GetAppDeveloperAsync(new DyGetAppDeveloperRequest { AppId = appId.ToString() });
                app = devResp.App;
                developer = devResp.Developer;
            }
            catch (RpcException)
            {
                // App or developer may have been removed; skip enrichment
            }
        }

        return Ok(PaymentOrderResponse.FromOrder(order, app, developer));
    }

    public class PayOrderRequest
    {
        public Guid? PayerWalletId { get; set; }
        public string? PinCode { get; set; }
    }

    [HttpPost("{id:guid}/pay")]
    [Authorize]
    public async Task<ActionResult<SnWalletOrder>> PayOrder(Guid id, [FromBody] PayOrderRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var currentAccountId = Guid.Parse(currentUser.Id);
            var pinCode = string.IsNullOrWhiteSpace(request.PinCode) ? NoPinProvided : request.PinCode;
            var wallet = await ResolveAccessiblePayerWalletAsync(currentAccountId, request.PayerWalletId);
            if (wallet == null)
                return BadRequest("Wallet was not found.");

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

        // Handle cancellation: cancel pending settlements and refund payer
        if (request.Status == Shared.Models.OrderStatus.Cancelled)
        {
            var merchantSvc = new MerchantService(db);
            await merchantSvc.CancelSettlementsByOrderAsync(id);

            // If the order was already paid, refund the payer
            if (order.Status == Shared.Models.OrderStatus.Paid)
            {
                // Load the transaction to get the payer wallet
                var tx = await db.PaymentTransactions.FindAsync(order.TransactionId);
                if (tx?.PayerWalletId != null)
                {
                    // ponytail: escrow → payee was never credited, so refund from system
                    var refundFromWallet = tx.PayeeWalletId; // null if escrow
                    await payment.CreateTransactionAsync(
                        payerWalletId: refundFromWallet,      // null = system pays
                        payeeWalletId: tx.PayerWalletId,       // back to original payer
                        currency: order.Currency,
                        amount: order.Amount,
                        remarks: $"Refund for cancelled order #{order.Id}",
                        type: Shared.Models.TransactionType.System,
                        silent: true);
                }
            }
        }

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

        var merchantWalletId = await ResolveMerchantWalletIdAsync(client);
        if (!merchantWalletId.HasValue)
            return BadRequest("Merchant payout wallet was not configured.");

        var payerWallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == merchantWalletId.Value);
        if (payerWallet is null)
            return BadRequest("Merchant payout wallet was not found.");

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

    private async Task<Guid?> ResolveMerchantWalletIdAsync(SnCustomApp client)
    {
        try
        {
            var devResp = await customApps.GetAppDeveloperAsync(new DyGetAppDeveloperRequest { AppId = client.Id.ToString() });
            if (!Guid.TryParse(devResp.Developer.PublisherId, out var publisherId))
                return null;

            var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.PublisherId == publisherId);
            return merchant?.PaymentWalletId;
        }
        catch (RpcException)
        {
            return null;
        }
    }

    private async Task<SnWallet?> ResolveAccessiblePayerWalletAsync(Guid currentAccountId, Guid? payerWalletId)
    {
        if (!payerWalletId.HasValue)
            return await ws.GetAccountWalletAsync(currentAccountId);

        var wallet = await ws.GetWalletAsync(payerWalletId.Value);
        if (wallet is null)
            return null;

        if (wallet.AccountId == currentAccountId)
            return wallet;

        if (!wallet.RealmId.HasValue)
            return null;

        var publisher = (await publishers.ListPublishers(realmId: wallet.RealmId.Value.ToString())).FirstOrDefault();
        if (publisher is null)
            return null;

        return await publishers.IsMemberWithRole(publisher.Id, currentAccountId, PublisherMemberRole.Editor)
            ? wallet
            : null;
    }
}
