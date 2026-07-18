using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Auth;
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
    RemotePublisherService publishers,
    RemoteAccountContactService remoteContacts
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
        if (client is null) return BadRequest(new ApiError { Code = "WALLET_ORDER_INVALID_CLIENT", Message = "Invalid client credentials.", Status = 400 });

        Guid? payeeWalletId = null;
        if (request.PayeeWalletId.HasValue)
        {
            var walletExists = await db.Wallets.AnyAsync(w => w.Id == request.PayeeWalletId.Value);
            if (!walletExists)
                return BadRequest(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Payee wallet was not found.", Status = 400 });

            payeeWalletId = request.PayeeWalletId.Value;
        }
        else
        {
            payeeWalletId = await ResolveMerchantWalletIdAsync(client);
            if (!payeeWalletId.HasValue)
                return BadRequest(new ApiError { Code = "WALLET_MERCHANT_NO_PAYOUT_WALLET", Message = "Merchant payout wallet was not configured.", Status = 400 });
        }

        // New: order with product line items
        if (request.Items is { Count: > 0 })
        {
            await payment.ExpireOverdueOrdersAsync();

            var items = new List<SnWalletOrderItem>();
            var products = new List<(OrderItemRequest Item, SnAppProduct Product)>();
            decimal totalAmount = 0;
            string? currency = null;

            foreach (var item in request.Items)
            {
                if (item.Quantity <= 0)
                    return BadRequest(new ApiError { Code = "WALLET_ORDER_INVALID_QUANTITY", Message = "Item quantity must be greater than zero.", Status = 400 });

                var productResp = await customApps.GetAppProductAsync(new DyGetAppProductRequest
                {
                    AppId = client.Id.ToString(),
                    Identifier = item.ProductIdentifier
                });
                if (productResp.Product is null)
                    return BadRequest(new ApiError { Code = "WALLET_ORDER_PRODUCT_NOT_FOUND", Message = $"Product '{item.ProductIdentifier}' not found for this app.", Status = 400 });

                var product = SnAppProduct.FromProto(productResp.Product);
                if (product.State?.IsEnabled == false)
                    return BadRequest(new ApiError { Code = "WALLET_ORDER_PRODUCT_NOT_SELLING", Message = $"Product '{item.ProductIdentifier}' is not selling right now.", Status = 400 });

                if (currency is null)
                    currency = product.Currency;
                else if (currency != product.Currency)
                    return BadRequest(new ApiError { Code = "WALLET_ORDER_CURRENCY_MISMATCH", Message = "All items must use the same currency.", Status = 400 });

                var stockError = await ValidateStockAsync(client.ResourceIdentifier, item.ProductIdentifier, product, item.Quantity);
                if (stockError is not null)
                    return BadRequest(new ApiError { Code = "WALLET_ORDER_STOCK_ERROR", Message = stockError, Status = 400 });

                var unitPrice = product.Price;
                var lineTotal = unitPrice * item.Quantity;

                items.Add(new SnWalletOrderItem
                {
                    ProductIdentifier = item.ProductIdentifier,
                    Quantity = item.Quantity,
                    UnitPrice = unitPrice,
                    Currency = product.Currency
                });
                products.Add((item, product));
                totalAmount += lineTotal;
            }

            Dictionary<string, object> meta;
            try
            {
                meta = BuildFulfillmentMeta(request.Meta, products);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ApiError { Code = "WALLET_ORDER_FULFILLMENT_META_FAILED", Message = ex.Message, Status = 400 });
            }
            var firstProduct = products[0].Product; // ponytail: single-currency order, recurring metadata follows first product
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
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset ExpiredAt { get; set; }
        public Guid? PayeeWalletId { get; set; }
        public Guid? TransactionId { get; set; }
        public List<SnWalletOrderItem> Items { get; set; } = new();

        public SnCustomApp? App { get; set; }
        public AppDeveloperResponse? Developer { get; set; }

        public static PaymentOrderResponse FromOrder(SnWalletOrder order, SnCustomApp? app, AppDeveloperResponse? developer)
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
                CreatedAt = ToDateTimeOffset(order.CreatedAt),
                UpdatedAt = ToDateTimeOffset(order.UpdatedAt),
                ExpiredAt = ToDateTimeOffset(order.ExpiredAt),
                PayeeWalletId = order.PayeeWalletId,
                TransactionId = order.TransactionId,
                Items = order.Items,
                App = app,
                Developer = developer
            };
        }

        private static DateTimeOffset ToDateTimeOffset(Instant instant) => new(instant.ToDateTimeUtc(), TimeSpan.Zero);
    }

    public class AppDeveloperResponse
    {
        public Guid Id { get; set; }
        public Guid PublisherId { get; set; }
        public string? PublisherName { get; set; }

        public static AppDeveloperResponse FromProto(DyDeveloper developer) => new()
        {
            Id = Guid.Parse(developer.Id),
            PublisherId = Guid.Parse(developer.PublisherId),
            PublisherName = string.IsNullOrWhiteSpace(developer.PublisherName) ? null : developer.PublisherName
        };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentOrderResponse>> GetOrderById(Guid id)
    {
        await payment.ExpireOverdueOrdersAsync();

        var order = await db.PaymentOrders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound(new ApiError { Code = "WALLET_ORDER_NOT_FOUND", Message = "Order was not found.", Status = 404 });

        SnCustomApp? app = null;
        AppDeveloperResponse? developer = null;
        if (!string.IsNullOrWhiteSpace(order.AppIdentifier) && Guid.TryParse(order.AppIdentifier.Replace("developer.app:", ""), out var appId))
        {
            try
            {
                var devResp = await customApps.GetAppDeveloperAsync(new DyGetAppDeveloperRequest { AppId = appId.ToString() });
                if (devResp.App is not null)
                    app = SnCustomApp.FromProtoValue(devResp.App);
                if (devResp.Developer is not null)
                    developer = AppDeveloperResponse.FromProto(devResp.Developer);
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
    [AskPermission(PermissionKeys.OrdersPay)]
    public async Task<ActionResult<SnWalletOrder>> PayOrder(Guid id, [FromBody] PayOrderRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var currentAccountId = Guid.Parse(currentUser.Id);
            var pinCode = string.IsNullOrWhiteSpace(request.PinCode) ? NoPinProvided : request.PinCode;
            var wallet = await ResolveAccessiblePayerWalletAsync(currentAccountId, request.PayerWalletId);
            if (wallet == null)
                return BadRequest(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Wallet was not found.", Status = 400 });

            var order = await db.PaymentOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id);
            if (order is null)
                return NotFound(new ApiError { Code = "WALLET_ORDER_NOT_FOUND", Message = "Order was not found.", Status = 404 });

            await ValidateOrderFulfillmentContactsAsync(currentAccountId, order);

            var paidOrder = await payment.PayOrderAsync(id, wallet);
            return Ok(paidOrder);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "WALLET_ORDER_PAY_FAILED", Message = ex.Message, Status = 400 });
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
        if (client is null) return BadRequest(new ApiError { Code = "WALLET_ORDER_INVALID_CLIENT", Message = "Invalid client credentials.", Status = 400 });

        var order = await db.PaymentOrders.FindAsync(id);

        if (order == null)
            return NotFound(new ApiError { Code = "WALLET_ORDER_NOT_FOUND", Message = "Order was not found.", Status = 404 });

        if (order.AppIdentifier != client.ResourceIdentifier)
        {
            return BadRequest(new ApiError { Code = "WALLET_ORDER_CLIENT_MISMATCH", Message = "Order does not belong to this client.", Status = 400 });
        }

        if (request.Status != Shared.Models.OrderStatus.Finished && request.Status != Shared.Models.OrderStatus.Cancelled)
            return BadRequest(new ApiError { Code = "WALLET_ORDER_INVALID_STATUS", Message = "Invalid status. Available statuses are Finished, Cancelled.", Status = 400 });

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
        if (client is null) return BadRequest(new ApiError { Code = "WALLET_ORDER_INVALID_CLIENT", Message = "Invalid client credentials.", Status = 400 });

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
        if (client is null) return BadRequest(new ApiError { Code = "WALLET_ORDER_INVALID_CLIENT", Message = "Invalid client credentials.", Status = 400 });

        var merchantWalletId = await ResolveMerchantWalletIdAsync(client);
        if (!merchantWalletId.HasValue)
            return BadRequest(new ApiError { Code = "WALLET_MERCHANT_NO_PAYOUT_WALLET", Message = "Merchant payout wallet was not configured.", Status = 400 });

        var payerWallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == merchantWalletId.Value);
        if (payerWallet is null)
            return BadRequest(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Merchant payout wallet was not found.", Status = 400 });

        if (payerWallet.AccountId == request.PayeeAccountId)
            return BadRequest(new ApiError { Code = "WALLET_PAYOUT_SELF_FORBIDDEN", Message = "Cannot pay the payout wallet owner.", Status = 400 });

        try
        {
            var payeeWallet = await db.Wallets.FirstOrDefaultAsync(w => w.AccountId == request.PayeeAccountId);
            if (payeeWallet is null)
                return BadRequest(new ApiError { Code = "WALLET_NOT_FOUND", Message = "Payee wallet was not found.", Status = 400 });

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
            return BadRequest(new ApiError { Code = "WALLET_PAYOUT_FAILED", Message = err.Message, Status = 400 });
        }
    }

    private async Task<string?> ValidateStockAsync(string appIdentifier, string productIdentifier, SnAppProduct product, int requestedQuantity)
    {
        var state = product.State;
        if (state is null || !state.IsEnabled)
            return $"Product '{product.Identifier}' is not selling right now.";

        if (state.StockMode == ProductStockMode.Unlimited)
            return null;

        if (!state.StockQuantity.HasValue || state.StockQuantity.Value < 0)
            return $"Product '{product.Identifier}' is missing stock configuration.";

        var now = SystemClock.Instance.GetCurrentInstant();
        var windowStart = GetStockWindowStart(state, now);
        var orderQuery = db.PaymentOrders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.AppIdentifier == appIdentifier)
            .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Expired)
            .Where(o => o.Items.Any(i => i.ProductIdentifier == productIdentifier));

        if (windowStart.HasValue)
            orderQuery = orderQuery.Where(o => o.CreatedAt >= windowStart.Value);

        // ponytail: expired unpaid orders are already excluded after ExpireOverdueOrdersAsync; paid/finished still consume stock.
        var reserved = await orderQuery
            .SelectMany(o => o.Items)
            .Where(i => i.ProductIdentifier == productIdentifier)
            .SumAsync(i => (int?)i.Quantity) ?? 0;

        var remaining = state.StockQuantity.Value - reserved;
        return remaining >= requestedQuantity
            ? null
            : $"Product '{product.Identifier}' does not have enough stock.";
    }

    private static Instant? GetStockWindowStart(SnAppProductState state, Instant now)
    {
        var utcNow = now.ToDateTimeUtc();
        return state.StockMode switch
        {
            ProductStockMode.Daily => Instant.FromDateTimeUtc(utcNow.Date),
            ProductStockMode.Weekly => Instant.FromDateTimeUtc(utcNow.Date.AddDays(-(int)((7 + (utcNow.DayOfWeek - DayOfWeek.Monday)) % 7))),
            ProductStockMode.Monthly => Instant.FromDateTimeUtc(new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc)),
            ProductStockMode.Yearly => Instant.FromDateTimeUtc(new DateTime(utcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            ProductStockMode.Manual => state.LastRestockedAt,
            _ => null
        };
    }

    private static Dictionary<string, object> BuildFulfillmentMeta(
        Dictionary<string, object>? requestMeta,
        List<(OrderItemRequest Item, SnAppProduct Product)> products)
    {
        var meta = requestMeta is null
            ? new Dictionary<string, object>()
            : new Dictionary<string, object>(requestMeta);

        if (!products.Any(x => x.Product.Fulfillment?.IsAddressRequired == true))
            return meta;

        if (!TryGetGuid(requestMeta, "address_contact_id", out var addressContactId))
            throw new InvalidOperationException("Address contact id is required for this order.");

        var addressSnapshot = TryGetString(requestMeta, "address_snapshot");
        if (string.IsNullOrWhiteSpace(addressSnapshot))
            throw new InvalidOperationException("Address snapshot is required for this order.");

        meta["fulfillment"] = new Dictionary<string, object>
        {
            ["address_contact_id"] = addressContactId.ToString(),
            ["address_snapshot"] = addressSnapshot,
            ["requires_address"] = true
        };

        return meta;
    }

    private async Task ValidateOrderFulfillmentContactsAsync(Guid accountId, SnWalletOrder order)
    {
        if (order.Meta is null || !order.Meta.TryGetValue("fulfillment", out var fulfillmentObj))
            return;

        var contactIdText = fulfillmentObj switch
        {
            JsonElement fulfillmentJson when fulfillmentJson.TryGetProperty("address_contact_id", out var contactIdProp) => contactIdProp.GetString(),
            Dictionary<string, object> fulfillmentMap when fulfillmentMap.TryGetValue("address_contact_id", out var contactIdObj) => contactIdObj?.ToString(),
            _ => null
        };
        if (!Guid.TryParse(contactIdText, out var contactId))
            throw new InvalidOperationException("Order is missing a valid address contact id.");

        var contacts = await remoteContacts.ListContactsAsync(accountId, AccountContactType.Address, cancellationToken: HttpContext.RequestAborted);
        var contact = contacts.FirstOrDefault(c => c.Id == contactId);
        if (contact is null)
            throw new InvalidOperationException("Address contact was not found for the current account.");

        order.Meta ??= new Dictionary<string, object>();
        order.Meta["fulfillment"] = new Dictionary<string, object>
        {
            ["address_contact_id"] = contact.Id.ToString(),
            ["address_snapshot"] = contact.Content,
            ["requires_address"] = true
        };
        await db.SaveChangesAsync();
    }

    private static bool TryGetGuid(Dictionary<string, object>? meta, string key, out Guid value)
    {
        value = Guid.Empty;
        var str = TryGetString(meta, key);
        return str is not null && Guid.TryParse(str, out value);
    }

    private static string? TryGetString(Dictionary<string, object>? meta, string key)
    {
        if (meta is null || !meta.TryGetValue(key, out var raw) || raw is null)
            return null;

        return raw switch
        {
            string str => str,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString(),
            JsonElement json => json.ToString(),
            _ => raw.ToString()
        };
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
