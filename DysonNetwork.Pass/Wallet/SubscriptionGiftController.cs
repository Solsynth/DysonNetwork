using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Wallet;

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var query = await subscriptions.GetGiftsByGifterAsync(currentUser.Id);
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var gifts = await subscriptions.GetGiftsByRecipientAsync(currentUser.Id);
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var gift = await db.WalletGifts
            .Include(g => g.Gifter).ThenInclude(a => a.Profile)
            .Include(g => g.Recipient).ThenInclude(a => a.Profile)
            .Include(g => g.Redeemer).ThenInclude(a => a.Profile)
            .Include(g => g.Subscription)
            .Include(g => g.Coupon)
            .FirstOrDefaultAsync(g => g.Id == giftId);

        if (gift is null) return NotFound();
        if (gift.GifterId != currentUser.Id && gift.RecipientId != currentUser.Id &&
            !(gift.IsOpenGift && gift.RedeemerId == currentUser.Id))
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

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
        else if (!gift.IsOpenGift && gift.RecipientId != currentUser.Id)
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
                    await subscriptions.GetSubscriptionAsync(currentUser.Id, subscriptionsInGroup);
                if (existingSubscription is not null)
                {
                    error = "You already have an active subscription of this type.";
                }
                else if (subscriptionInfo.RequiredLevel > 0)
                {
                    var profile = await db.AccountProfiles.FirstOrDefaultAsync(p => p.AccountId == currentUser.Id);
                    if (profile is null || profile.Level < subscriptionInfo.RequiredLevel)
                    {
                        error =
                            $"Account level must be at least {subscriptionInfo.RequiredLevel} to redeem this gift.";
                    }
                    else
                    {
                        canRedeem = true;
                    }
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

    /// <summary>
    /// Purchases a gift subscription.
    /// </summary>
    [HttpPost("purchase")]
    [Authorize]
    public async Task<ActionResult<SnWalletGift>> PurchaseGift([FromBody] PurchaseGiftRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var gift = await subscriptions.MarkGiftAsSentAsync(giftId, currentUser.Id);
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var gift = await subscriptions.CancelGiftAsync(giftId, currentUser.Id);
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
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var order = await subscriptions.CreateGiftOrder(currentUser.Id, giftId);
            return order;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }


}
