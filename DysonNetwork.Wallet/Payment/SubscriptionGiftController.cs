using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Wallet.Payment;

[ApiController]
[Route("/api/subscriptions/gifts")]
public class SubscriptionGiftController(
    SubscriptionService subscriptions,
    AppDatabase db
) : ControllerBase
{
    /// <summary>
    /// Lists gifts purchased by the current user.
    /// </summary>
    [HttpGet("sent")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletGift>>> ListSentGifts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var query = await subscriptions.GetGiftsByGifterAsync(Guid.Parse(currentUser.Id));
        var totalCount = query.Count;

        var gifts = query
            .Skip(offset)
            .Take(take)
            .ToList();

        Response.Headers["X-Total"] = totalCount.ToString();

        return gifts;
    }

    /// <summary>
    /// Lists gifts received by the current user (both direct and redeemed open gifts).
    /// </summary>
    [HttpGet("received")]
    [Authorize]
    public async Task<ActionResult<List<SnWalletGift>>> ListReceivedGifts(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var gifts = await subscriptions.GetGiftsByRecipientAsync(Guid.Parse(currentUser.Id));
        var totalCount = gifts.Count;

        gifts = gifts
            .Skip(offset)
            .Take(take)
            .ToList();

        Response.Headers["X-Total"] = totalCount.ToString();

        return gifts;
    }

    /// <summary>
    /// Gets a specific gift by ID (only if user is the gifter or recipient).
    /// </summary>
    [HttpGet("{giftId}")]
    [Authorize]
    public async Task<ActionResult<SnWalletGift>> GetGift(Guid giftId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var currentUserId = Guid.Parse(currentUser.Id);
        var gift = await db.WalletGifts
            .Include(g => g.Subscription)
            .Include(g => g.Coupon)
            .FirstOrDefaultAsync(g => g.Id == giftId);

        if (gift is null) return NotFound();
        if (gift.GifterId != currentUserId && gift.RecipientId != currentUserId &&
            !(gift.IsOpenGift && gift.RedeemerId == currentUserId))
            return NotFound();

        return gift;
    }

    /// <summary>
    /// Checks if a gift code is valid and redeemable.
    /// </summary>
    [HttpGet("check/{giftCode}")]
    [Authorize]
    public async Task<ActionResult<GiftCheckResponse>> CheckGiftCode(string giftCode)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var gift = await subscriptions.GetGiftByCodeAsync(giftCode);
        if (gift is null) return NotFound("Gift code not found.");

        var canRedeem = false;
        var error = "";

        if (gift.Status != DysonNetwork.Shared.Models.GiftStatus.Sent)
        {
            error = gift.Status switch
            {
                DysonNetwork.Shared.Models.GiftStatus.Created => "Gift has not been sent yet.",
                DysonNetwork.Shared.Models.GiftStatus.Redeemed => "Gift has already been redeemed.",
                DysonNetwork.Shared.Models.GiftStatus.Expired => "Gift has expired.",
                DysonNetwork.Shared.Models.GiftStatus.Cancelled => "Gift has been cancelled.",
                _ => "Gift is not redeemable."
            };
        }
        else if (gift.ExpiresAt < SystemClock.Instance.GetCurrentInstant())
        {
            error = "Gift has expired.";
        }
        else if (!gift.IsOpenGift && gift.RecipientId != Guid.Parse(currentUser.Id))
        {
            error = "This gift is intended for someone else.";
        }
        else
        {
            // Check if user already has this subscription type
            var subscriptionInfo = SubscriptionTypeData
                .SubscriptionDict.TryGetValue(gift.SubscriptionIdentifier, out var template)
                ? template
                : null;

            if (subscriptionInfo != null)
            {
                var subscriptionsInGroup = subscriptionInfo.GroupIdentifier is not null
                    ? SubscriptionTypeData.SubscriptionDict
                        .Where(s => s.Value.GroupIdentifier == subscriptionInfo.GroupIdentifier)
                        .Select(s => s.Value.Identifier)
                        .ToArray()
                    : [gift.SubscriptionIdentifier];

                var existingSubscription =
                    await subscriptions.GetSubscriptionAsync(Guid.Parse(currentUser.Id), subscriptionsInGroup);
                if (existingSubscription is not null)
                {
                    error = "You already have an active subscription of this type.";
                }
                else
                {
                    canRedeem = true;
                }
            }
        }

        return new GiftCheckResponse
        {
            GiftCode = giftCode,
            SubscriptionIdentifier = gift.SubscriptionIdentifier,
            CanRedeem = canRedeem,
            Error = error,
            Message = gift.Message
        };
    }

    public class GiftCheckResponse
    {
        public string GiftCode { get; set; } = null!;
        public string SubscriptionIdentifier { get; set; } = null!;
        public bool CanRedeem { get; set; }
        public string Error { get; set; } = null!;
        public string? Message { get; set; }
    }

    public class PurchaseGiftRequest
    {
        [Required] public string SubscriptionIdentifier { get; set; } = null!;
        public Guid? RecipientId { get; set; }
        [Required] public string PaymentMethod { get; set; } = null!;
        [Required] public SnPaymentDetails PaymentDetails { get; set; } = null!;
        public string? Message { get; set; }
        public string? Coupon { get; set; }
        public int? GiftDurationDays { get; set; } = 30; // Gift expires in 30 days by default
        public int? SubscriptionDurationDays { get; set; } = 30; // Subscription lasts 30 days when redeemed
    }

    const int MinimumAccountLevel = 60;

    /// <summary>
    /// Purchases a gift subscription.
    /// </summary>
    [HttpPost("purchase")]
    [Authorize]
    public async Task<ActionResult<SnWalletGift>> PurchaseGift([FromBody] PurchaseGiftRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        if (currentUser.Profile.Level < MinimumAccountLevel)
        {
            if (currentUser.PerkSubscription is null)
                return StatusCode(403, "Account level must be at least 60 or a member of the Stellar Program to purchase a gift.");
        }

        Duration? giftDuration = null;
        if (request.GiftDurationDays.HasValue)
            giftDuration = Duration.FromDays(request.GiftDurationDays.Value);

        Duration? subscriptionDuration = null;
        if (request.SubscriptionDurationDays.HasValue)
            subscriptionDuration = Duration.FromDays(request.SubscriptionDurationDays.Value);

        try
        {
            var gift = await subscriptions.PurchaseGiftAsync(
                currentUser,
                request.RecipientId,
                request.SubscriptionIdentifier,
                request.PaymentMethod,
                request.PaymentDetails,
                request.Message,
                request.Coupon,
                giftDuration,
                subscriptionDuration
            );

            return gift;
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

    public class RedeemGiftRequest
    {
        [Required] public string GiftCode { get; set; } = null!;
    }

    /// <summary>
    /// Redeems a gift using its code, creating a subscription for the current user.
    /// </summary>
    [HttpPost("redeem")]
    [Authorize]
    public async Task<ActionResult<RedeemGiftResponse>> RedeemGift([FromBody] RedeemGiftRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        try
        {
            var (gift, subscription) = await subscriptions.RedeemGiftAsync(currentUser, request.GiftCode);

            return new RedeemGiftResponse
            {
                Gift = gift,
                Subscription = subscription
            };
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    public class RedeemGiftResponse
    {
        public SnWalletGift Gift { get; set; } = null!;
        public SnWalletSubscription Subscription { get; set; } = null!;
    }

    /// <summary>
    /// Marks a gift as sent (ready for redemption).
    /// </summary>
    [HttpPost("{giftId}/send")]
    [Authorize]
    public async Task<ActionResult<SnWalletGift>> SendGift(Guid giftId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        try
        {
            var gift = await subscriptions.MarkGiftAsSentAsync(giftId, Guid.Parse(currentUser.Id));
            return gift;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Cancels a gift before it's redeemed.
    /// </summary>
    [HttpPost("{giftId}/cancel")]
    [Authorize]
    public async Task<ActionResult<SnWalletGift>> CancelGift(Guid giftId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        try
        {
            var gift = await subscriptions.CancelGiftAsync(giftId, Guid.Parse(currentUser.Id));
            return gift;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Creates an order for an unpaid gift.
    /// </summary>
    [HttpPost("{giftId}/order")]
    [Authorize]
    public async Task<ActionResult<SnWalletOrder>> CreateGiftOrder(Guid giftId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        try
        {
            var order = await subscriptions.CreateGiftOrder(Guid.Parse(currentUser.Id), giftId);
            return order;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
