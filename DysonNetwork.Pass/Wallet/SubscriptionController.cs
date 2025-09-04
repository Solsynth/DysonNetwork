using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Pass.Wallet.PaymentHandlers;

namespace DysonNetwork.Pass.Wallet;

[ApiController]
[Route("/api/subscriptions")]
public class SubscriptionController(SubscriptionService subscriptions, AfdianPaymentHandler afdian, AppDatabase db)
    : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<Subscription>>> ListSubscriptions(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var query = db.WalletSubscriptions.AsQueryable()
            .Where(s => s.AccountId == currentUser.Id)
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
    public async Task<ActionResult<Subscription>> GetSubscriptionFuzzy(string prefix)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var subscription = await db.WalletSubscriptions
            .Where(s => s.AccountId == currentUser.Id && s.IsActive)
            .Where(s => EF.Functions.ILike(s.Identifier, prefix + "%"))
            .OrderByDescending(s => s.BegunAt)
            .FirstOrDefaultAsync();
        if (subscription is null || !subscription.IsAvailable) return NotFound();

        return Ok(subscription);
    }

    [HttpGet("{identifier}")]
    [Authorize]
    public async Task<ActionResult<Subscription>> GetSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var subscription = await subscriptions.GetSubscriptionAsync(currentUser.Id, identifier);
        if (subscription is null) return NotFound($"Subscription with identifier {identifier} was not found.");

        return subscription;
    }

    public class CreateSubscriptionRequest
    {
        [Required] public string Identifier { get; set; } = null!;
        [Required] public string PaymentMethod { get; set; } = null!;
        [Required] public PaymentDetails PaymentDetails { get; set; } = null!;
        public string? Coupon { get; set; }
        public int? CycleDurationDays { get; set; }
        public bool IsFreeTrial { get; set; } = false;
        public bool IsAutoRenewal { get; set; } = true;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<Subscription>> CreateSubscription(
        [FromBody] CreateSubscriptionRequest request,
        [FromHeader(Name = "X-Noop")] bool noop = false
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

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
    public async Task<ActionResult<Subscription>> CancelSubscription(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        try
        {
            var subscription = await subscriptions.CancelSubscriptionAsync(currentUser.Id, identifier);
            return subscription;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{identifier}/order")]
    [Authorize]
    public async Task<ActionResult<Order>> CreateSubscriptionOrder(string identifier)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        try
        {
            var order = await subscriptions.CreateSubscriptionOrder(currentUser.Id, identifier);
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

    [HttpPost("order/restore/afdian")]
    [Authorize]
    public async Task<IActionResult> RestorePurchaseFromAfdian([FromBody] RestorePurchaseRequest request)
    {
        var order = await afdian.GetOrderAsync(request.OrderId);
        if (order is null) return NotFound($"Order with ID {request.OrderId} was not found.");

        var subscription = await subscriptions.CreateSubscriptionFromOrder(order);
        return Ok(subscription);
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
}