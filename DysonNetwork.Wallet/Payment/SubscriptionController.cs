using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Security.Cryptography;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Wallet.Payment.PaymentHandlers;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/subscriptions")]
public class SubscriptionController(
    SubscriptionService subscriptions,
    SubscriptionCatalogService catalog,
    AfdianPaymentHandler afdian,
    AppleStorePaymentHandler appleStore,
    PaddlePaymentHandler paddle,
    AppDatabase db,
    RemoteActionLogService als
)
    : ControllerBase
{
    public class SubscriptionCatalogItem
    {
        public string Identifier { get; set; } = null!;
        public string? GroupIdentifier { get; set; }
        public string DisplayName { get; set; } = null!;
        public string Currency { get; set; } = null!;
        public decimal BasePrice { get; set; }
        public int PerkLevel { get; set; }
        public int? MinimumAccountLevel { get; set; }
        public decimal? ExperienceMultiplier { get; set; }
        public int? GoldenPointReward { get; set; }
        public SubscriptionDisplayConfig? DisplayConfig { get; set; }
        public List<string> AllowedPaymentMethods { get; set; } = [];
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<List<SubscriptionCatalogItem>>> ListCatalog()
    {
        var definitions = await catalog.ListDefinitionsAsync(HttpContext.RequestAborted);
        return Ok(definitions.Select(def => new SubscriptionCatalogItem
        {
            Identifier = def.Identifier,
            GroupIdentifier = def.GroupIdentifier,
            DisplayName = def.DisplayName,
            Currency = def.Currency,
            BasePrice = def.BasePrice,
            PerkLevel = def.PerkLevel,
            MinimumAccountLevel = def.MinimumAccountLevel,
            ExperienceMultiplier = def.ExperienceMultiplier,
            GoldenPointReward = def.GoldenPointReward,
            DisplayConfig = def.DisplayConfig?.Clone(),
            AllowedPaymentMethods = def.PaymentPolicy.AllowedMethods.ToList()
        }).ToList());
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnWalletSubscription>>> ListSubscriptions(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var query = db.WalletSubscriptions.AsQueryable()
            .Where(s => s.AccountId == Guid.Parse(currentUser.Id))
            .Include(s => s.Coupon)
            .OrderByDescending(s => s.BegunAt);

        var totalCount = await query.CountAsync();

        var subscriptionsList = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();

        return subscriptionsList;
    }

    [HttpGet("fuzzy/{prefix}")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> GetSubscriptionFuzzy(string prefix)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == Guid.Parse(currentUser.Id) && s.IsActive)
            .Where(s => EF.Functions.ILike(s.Identifier, prefix + "%"))
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();
        if (subscription is null || !subscription.IsAvailable) return NotFound();

        return Ok(subscription);
    }

    [HttpGet("{identifier}")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> GetSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var subscription = await subscriptions.GetSubscriptionAsync(Guid.Parse(currentUser.Id), identifier);
        if (subscription is null) return NotFound($"Subscription with identifier {identifier} was not found.");

        return subscription;
    }

    public class CreateSubscriptionRequest
    {
        [Required] public string Identifier { get; set; } = null!;
        [Required] public string PaymentMethod { get; set; } = null!;
        [Required] public SnPaymentDetails PaymentDetails { get; set; } = null!;
        public string? Coupon { get; set; }
        public int? CycleDurationDays { get; set; }
        public bool IsFreeTrial { get; set; } = false;
        public bool IsAutoRenewal { get; set; } = true;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> CreateSubscription(
        [FromBody] CreateSubscriptionRequest request,
        [FromHeader(Name = "X-Noop")] bool noop = false
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        Duration? cycleDuration = null;
        if (request.CycleDurationDays.HasValue)
            cycleDuration = Duration.FromDays(request.CycleDurationDays.Value);

        try
        {
            var subscription = await subscriptions.CreateSubscriptionAsync(
                currentUser,
                request.Identifier,
                request.PaymentMethod,
                request.PaymentDetails,
                cycleDuration,
                request.Coupon,
                request.IsFreeTrial,
                request.IsAutoRenewal,
                noop
            );

            als.CreateActionLog(
                Guid.Parse(currentUser.Id),
                "subscriptions.create",
                new Dictionary<string, object>
                {
                    { "subscription_identifier", request.Identifier },
                    { "payment_method", request.PaymentMethod },
                    { "is_free_trial", request.IsFreeTrial }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return subscription;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{identifier}/cancel")]
    [Authorize]
    public async Task<ActionResult<SnWalletSubscription>> CancelSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var subscription = await subscriptions.CancelSubscriptionAsync(Guid.Parse(currentUser.Id), identifier);

            als.CreateActionLog(
                Guid.Parse(currentUser.Id),
                "subscriptions.cancel",
                new Dictionary<string, object>
                {
                    { "subscription_identifier", identifier }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return subscription;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{identifier}/order")]
    [Authorize]
    public async Task<ActionResult<SnWalletOrder>> CreateSubscriptionOrder(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var order = await subscriptions.CreateSubscriptionOrder(Guid.Parse(currentUser.Id), identifier);
            return order;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public class RestorePurchaseRequest
    {
        [Required] public string OrderId { get; set; } = null!;
    }

    public class RestoreApplePurchaseRequest
    {
        [Required] public string SignedTransactionInfo { get; set; } = null!;
    }

    public class CreatePaddleCheckoutRequest
    {
        public string? ProviderReferenceId { get; set; }
    }

    public class PaddleCheckoutResponse
    {
        public string TransactionId { get; set; } = null!;
        public string CheckoutUrl { get; set; } = null!;
        public string ProviderReferenceId { get; set; } = null!;
    }

    [HttpPost("order/restore/afdian")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromAfdian([FromBody] RestorePurchaseRequest request)
    {
        var order = await afdian.GetOrderAsync(request.OrderId);
        if (order is null) return NotFound($"Order with ID {request.OrderId} was not found.");

        var subscription = await subscriptions.CreateSubscriptionFromOrder(order);
        return Ok(subscription);
    }

    [HttpPost("order/restore/paddle")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromPaddle([FromBody] RestorePurchaseRequest request)
    {
        var order = await paddle.GetTransactionAsync(request.OrderId, HttpContext.RequestAborted);
        if (order is null) return NotFound($"Transaction with ID {request.OrderId} was not found.");

        var subscription = await subscriptions.CreateSubscriptionFromOrder(order);
        return Ok(subscription);
    }

    [HttpPost("order/restore/apple")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromAppleStore([FromBody] RestoreApplePurchaseRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        AppleAppStoreTransaction transaction;
        try
        {
            transaction = appleStore.ParseSignedTransaction(request.SignedTransactionInfo);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or CryptographicException)
        {
            return BadRequest(ex.Message);
        }

        if (!string.Equals(transaction.AccountId, currentUser.Id, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Apple transaction account token does not match the current user.");

        var subscription = await subscriptions.CreateSubscriptionFromOrder(transaction);
        return Ok(subscription);
    }

    [HttpPost("{identifier}/checkout/paddle")]
    [Authorize]
    public async Task<ActionResult<PaddleCheckoutResponse>> CreatePaddleCheckout(
        string identifier,
        [FromBody] CreatePaddleCheckoutRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        try
        {
            var (definition, providerReference) = await subscriptions.PreparePaddleCheckoutAsync(
                currentUser,
                identifier,
                request?.ProviderReferenceId,
                HttpContext.RequestAborted
            );

            var session = await paddle.CreateCheckoutAsync(
                providerReference,
                new Dictionary<string, object>
                {
                    ["account_id"] = currentUser.Id,
                    ["subscription_identifier"] = definition.Identifier,
                    ["price_id"] = providerReference,
                    ["group_identifier"] = definition.GroupIdentifier ?? string.Empty
                },
                HttpContext.RequestAborted
            );

            return Ok(new PaddleCheckoutResponse
            {
                TransactionId = session.TransactionId,
                CheckoutUrl = session.CheckoutUrl,
                ProviderReferenceId = providerReference
            });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("order/handle/afdian")]
    public async Task<ActionResult<WebhookResponse>> AfdianWebhook()
    {
        var response = await afdian.HandleWebhook(Request, async webhookData =>
        {
            var order = webhookData.Order;
            await subscriptions.CreateSubscriptionFromOrder(order);
        });

        return Ok(response);
    }

    [HttpPost("order/handle/paddle")]
    public async Task<IActionResult> PaddleWebhook()
    {
        var response = await paddle.HandleWebhook(Request, async transaction =>
        {
            await subscriptions.CreateSubscriptionFromOrder(transaction);
        }, HttpContext.RequestAborted);

        return response.IsSuccess ? Ok() : Unauthorized();
    }

    [HttpPost("order/handle/apple")]
    public async Task<IActionResult> AppleStoreWebhook()
    {
        var response = await appleStore.HandleWebhook(Request, async transaction =>
        {
            await subscriptions.CreateSubscriptionFromOrder(transaction);
        }, HttpContext.RequestAborted);

        return response.IsSuccess ? Ok() : Unauthorized();
    }
}
