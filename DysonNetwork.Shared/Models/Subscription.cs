using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Models;

public record class SubscriptionTypeData(
    string Identifier,
    string? GroupIdentifier,
    string Currency,
    decimal BasePrice,
    int? RequiredLevel = null
)
{
    public static readonly Dictionary<string, SubscriptionTypeData> SubscriptionDict =
        new()
        {
            [SubscriptionType.Twinkle] = new SubscriptionTypeData(
                SubscriptionType.Twinkle,
                SubscriptionType.StellarProgram,
                WalletCurrency.SourcePoint,
                0,
                1
            ),
            [SubscriptionType.Stellar] = new SubscriptionTypeData(
                SubscriptionType.Stellar,
                SubscriptionType.StellarProgram,
                WalletCurrency.SourcePoint,
                1200,
                20
            ),
            [SubscriptionType.Nova] = new SubscriptionTypeData(
                SubscriptionType.Nova,
                SubscriptionType.StellarProgram,
                WalletCurrency.SourcePoint,
                2400,
                40
            ),
            [SubscriptionType.Supernova] = new SubscriptionTypeData(
                SubscriptionType.Supernova,
                SubscriptionType.StellarProgram,
                WalletCurrency.SourcePoint,
                3600,
                60
            )
        };

    public static readonly Dictionary<string, string> SubscriptionHumanReadable =
        new()
        {
            [SubscriptionType.Twinkle] = "Stellar Program Twinkle",
            [SubscriptionType.Stellar] = "Stellar Program",
            [SubscriptionType.Nova] = "Stellar Program Nova",
            [SubscriptionType.Supernova] = "Stellar Program Supernova"
        };
}

/// <summary>
/// Represents a gifted subscription that can be claimed by another user.
/// Support both direct gifts (to specific users) and open gifts (anyone can redeem via link/code).
/// </summary>
[Index(nameof(GiftCode))]
[Index(nameof(GifterId))]
[Index(nameof(RecipientId))]
public class SnWalletGift : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The user who purchased/gave the gift.
    /// </summary>
    public Guid GifterId { get; set; }
    [NotMapped] public SnAccount Gifter { get; set; } = null!;

    /// <summary>
    /// The intended recipient. Null for open gifts that anyone can redeem.
    /// </summary>
    public Guid? RecipientId { get; set; }
    [NotMapped] public SnAccount? Recipient { get; set; }

    /// <summary>
    /// Unique redemption code/link identifier for the gift.
    /// </summary>
    [MaxLength(128)]
    public string GiftCode { get; set; } = null!;

    /// <summary>
    /// Optional custom message from the gifter.
    /// </summary>
    [MaxLength(1000)]
    public string? Message { get; set; }

    /// <summary>
    /// The subscription type being gifted.
    /// </summary>
    [MaxLength(4096)]
    public string SubscriptionIdentifier { get; set; } = null!;

    /// <summary>
    /// The original price before any discounts.
    /// </summary>
    public decimal BasePrice { get; set; }

    /// <summary>
    /// The final price paid after discounts.
    /// </summary>
    public decimal FinalPrice { get; set; }

    /// <summary>
    /// Current status of the gift.
    /// </summary>
    public GiftStatus Status { get; set; } = GiftStatus.Created;

    /// <summary>
    /// When the gift was redeemed. Null if not yet redeemed.
    /// </summary>
    public Instant? RedeemedAt { get; set; }

    /// <summary>
    /// The user who redeemed the gift (if different from recipient).
    /// </summary>
    public Guid? RedeemerId { get; set; }
    [NotMapped] public SnAccount? Redeemer { get; set; }

    /// <summary>
    /// The subscription created when the gift is redeemed.
    /// </summary>
    [JsonIgnore] public SnWalletSubscription? Subscription { get; set; }
    public Guid? SubscriptionId { get; set; }

    /// <summary>
    /// When the gift expires and can no longer be redeemed.
    /// </summary>
    public Instant ExpiresAt { get; set; }

    /// <summary>
    /// Whether this gift can be redeemed by anyone (open gift) or only the specified recipient.
    /// </summary>
    public bool IsOpenGift { get; set; }

    /// <summary>
    /// Payment method used by the gifter.
    /// </summary>
    [MaxLength(4096)]
    public string PaymentMethod { get; set; } = null!;

    [Column(TypeName = "jsonb")]
    public SnPaymentDetails PaymentDetails { get; set; } = null!;

    /// <summary>
    /// Coupon used for the gift purchase.
    /// </summary>
    public Guid? CouponId { get; set; }
    public SnWalletCoupon? Coupon { get; set; }

    /// <summary>
    /// Checks if the gift can still be redeemed.
    /// </summary>
    [NotMapped]
    public bool IsRedeemable
    {
        get
        {
            if (Status != GiftStatus.Sent) return false;
            var now = SystemClock.Instance.GetCurrentInstant();
            return now <= ExpiresAt;
        }
    }

    /// <summary>
    /// Checks if the gift has expired.
    /// </summary>
    [NotMapped]
    public bool IsExpired
    {
        get
        {
            if (Status == GiftStatus.Redeemed || Status == GiftStatus.Cancelled) return false;
            var now = SystemClock.Instance.GetCurrentInstant();
            return now > ExpiresAt;
        }
    }

     public DyGift ToProtoValue() => new()
     {
         Id = Id.ToString(),
         GifterId = GifterId.ToString(),
         RecipientId = RecipientId?.ToString(),
         GiftCode = GiftCode,
         Message = Message,
         SubscriptionIdentifier = SubscriptionIdentifier,
         BasePrice = BasePrice.ToString(CultureInfo.InvariantCulture),
         FinalPrice = FinalPrice.ToString(CultureInfo.InvariantCulture),
         Status = (DyGiftStatus)Status,
         RedeemedAt = RedeemedAt?.ToTimestamp(),
         RedeemerId = RedeemerId?.ToString(),
         SubscriptionId = SubscriptionId?.ToString(),
         ExpiresAt = ExpiresAt.ToTimestamp(),
         IsOpenGift = IsOpenGift,
         PaymentMethod = PaymentMethod,
         PaymentDetails = PaymentDetails.ToProtoValue(),
         CouponId = CouponId?.ToString(),
         Coupon = Coupon?.ToProtoValue(),
         IsRedeemable = IsRedeemable,
         IsExpired = IsExpired,
         CreatedAt = CreatedAt.ToTimestamp(),
         UpdatedAt = UpdatedAt.ToTimestamp()
     };

     public static SnWalletGift FromProtoValue(DyGift proto) => new()
     {
         Id = Guid.Parse(proto.Id),
         GifterId = Guid.Parse(proto.GifterId),
         RecipientId = proto.HasRecipientId ? Guid.Parse(proto.RecipientId) : null,
         GiftCode = proto.GiftCode,
         Message = proto.Message,
         SubscriptionIdentifier = proto.SubscriptionIdentifier,
         BasePrice = decimal.Parse(proto.BasePrice),
         FinalPrice = decimal.Parse(proto.FinalPrice),
         Status = (GiftStatus)proto.Status,
         RedeemedAt = proto.RedeemedAt?.ToInstant(),
         RedeemerId = proto.HasRedeemerId ? Guid.Parse(proto.RedeemerId) : null,
         SubscriptionId = proto.HasSubscriptionId ? Guid.Parse(proto.SubscriptionId) : null,
         ExpiresAt = proto.ExpiresAt.ToInstant(),
         IsOpenGift = proto.IsOpenGift,
         PaymentMethod = proto.PaymentMethod,
         PaymentDetails = SnPaymentDetails.FromProtoValue(proto.PaymentDetails),
         CouponId = proto.HasCouponId ? Guid.Parse(proto.CouponId) : null,
         Coupon = proto.Coupon is not null ? SnWalletCoupon.FromProtoValue(proto.Coupon) : null,
         CreatedAt = proto.CreatedAt.ToInstant(),
         UpdatedAt = proto.UpdatedAt.ToInstant()
     };
}

public abstract class SubscriptionType
{
    /// <summary>
    /// DO NOT USE THIS TYPE DIRECTLY,
    /// this is the prefix of all the stellar program subscriptions.
    /// </summary>
    public const string StellarProgram = "solian.stellar";

    /// <summary>
    /// No actual usage, just tells there is a free level named twinkle.
    /// Applies to every registered user by default, so there is no need to create a record in db for that.
    /// </summary>
    public const string Twinkle = "solian.stellar.twinkle";

    public const string Stellar = "solian.stellar.primary";
    public const string Nova = "solian.stellar.nova";
    public const string Supernova = "solian.stellar.supernova";
}

public abstract class SubscriptionPaymentMethod
{
    /// <summary>
    /// The solar points / solar dollars.
    /// </summary>
    public const string InAppWallet = "solian.wallet";

    /// <summary>
    /// afdian.com
    /// aka. China patreon
    /// </summary>
    public const string Afdian = "afdian";
}

public enum SubscriptionStatus
{
    Unpaid,
    Active,
    Expired,
    Cancelled
}

public enum GiftStatus
{
    Created = 0,
    Sent = 1,
    Redeemed = 2,
    Expired = 3,
    Cancelled = 4
}

/// <summary>
/// The subscription is for the Stellar Program in most cases.
/// The paid subscription in another word.
/// </summary>
[Index(nameof(Identifier))]
[Index(nameof(AccountId))]
[Index(nameof(Status))]
[Index(nameof(AccountId), nameof(Identifier))]
[Index(nameof(AccountId), nameof(IsActive))]
public class SnWalletSubscription : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Instant BegunAt { get; set; }
    public Instant? EndedAt { get; set; }

    /// <summary>
    /// The type of the subscriptions
    /// </summary>
    [MaxLength(4096)]
    public string Identifier { get; set; } = null!;

    /// <summary>
    /// The field is used to override the activation status of the membership.
    /// Might be used for refund handling and other special cases.
    ///
    /// Go see the IsAvailable field if you want to get real the status of the membership.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Indicates is the current user got the membership for free,
    /// to prevent giving the same discount for the same user again.
    /// </summary>
    public bool IsFreeTrial { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Unpaid;

    [MaxLength(4096)] public string PaymentMethod { get; set; } = null!;
    [Column(TypeName = "jsonb")] public SnPaymentDetails PaymentDetails { get; set; } = null!;
    public decimal BasePrice { get; set; }
    public Guid? CouponId { get; set; }
    public SnWalletCoupon? Coupon { get; set; }
    public Instant? RenewalAt { get; set; }

    public Guid AccountId { get; set; }
    [NotMapped] public SnAccount Account { get; set; } = null!;

    /// <summary>
    /// If this subscription was redeemed from a gift, this references the gift record.
    /// </summary>
    public SnWalletGift? Gift { get; set; }

    [NotMapped]
    public bool IsAvailable
    {
        get => IsAvailableAt(SystemClock.Instance.GetCurrentInstant());
    }

    [NotMapped]
    public decimal FinalPrice
    {
        get => CalculateFinalPriceAt(SystemClock.Instance.GetCurrentInstant());
    }

    /// <summary>
    /// Optimized method to check availability at a specific instant (avoids repeated SystemClock calls).
    /// </summary>
    public bool IsAvailableAt(Instant currentInstant)
    {
        if (!IsActive) return false;
        if (BegunAt > currentInstant) return false;
        if (EndedAt.HasValue && currentInstant > EndedAt.Value) return false;
        if (RenewalAt.HasValue && currentInstant > RenewalAt.Value) return false;
        if (Status != SubscriptionStatus.Active) return false;
        return true;
    }

    /// <summary>
    /// Optimized method to calculate final price at a specific instant (avoids repeated SystemClock calls).
    /// </summary>
    public decimal CalculateFinalPriceAt(Instant currentInstant)
    {
        if (IsFreeTrial) return 0;
        if (Coupon == null) return BasePrice;

        if (Coupon.AffectedAt.HasValue && currentInstant < Coupon.AffectedAt.Value ||
            Coupon.ExpiredAt.HasValue && currentInstant > Coupon.ExpiredAt.Value) return BasePrice;

        if (Coupon.DiscountAmount.HasValue) return BasePrice - Coupon.DiscountAmount.Value;
        if (Coupon.DiscountRate.HasValue) return BasePrice * (decimal)(1 - Coupon.DiscountRate.Value);
        return BasePrice;
    }

    /// <summary>
    /// Returns a reference object that contains a subset of subscription data
    /// suitable for client-side use, with sensitive information removed.
    /// </summary>
    public SnSubscriptionReferenceObject ToReference()
    {
        // Cache the current instant once to avoid multiple SystemClock calls
        var currentInstant = SystemClock.Instance.GetCurrentInstant();

        return new SnSubscriptionReferenceObject
        {
            Id = Id,
            Identifier = Identifier,
            BegunAt = BegunAt,
            EndedAt = EndedAt,
            IsActive = IsActive,
            IsAvailable = IsAvailableAt(currentInstant),
            IsFreeTrial = IsFreeTrial,
            Status = Status,
            BasePrice = BasePrice,
            FinalPrice = CalculateFinalPriceAt(currentInstant),
            RenewalAt = RenewalAt,
            AccountId = AccountId
        };
    }

    public DySubscription ToProtoValue()
    {
        var proto = new DySubscription
        {
            Id = Id.ToString(),
            BegunAt = BegunAt.ToTimestamp(),
            EndedAt = EndedAt?.ToTimestamp(),
            Identifier = Identifier,
            IsActive = IsActive,
            IsFreeTrial = IsFreeTrial,
            Status = (DySubscriptionStatus)Status,
            PaymentMethod = PaymentMethod,
            PaymentDetails = PaymentDetails.ToProtoValue(),
            BasePrice = BasePrice.ToString(CultureInfo.InvariantCulture),
            RenewalAt = RenewalAt?.ToTimestamp(),
            AccountId = AccountId.ToString(),
            IsAvailable = IsAvailable,
            FinalPrice = FinalPrice.ToString(CultureInfo.InvariantCulture),
            CreatedAt = CreatedAt.ToTimestamp(),
            UpdatedAt = UpdatedAt.ToTimestamp()
        };

        if (CouponId.HasValue)
        {
            proto.CouponId = CouponId.Value.ToString();
        }

        if (Coupon != null)
        {
            proto.Coupon = Coupon.ToProtoValue();
        }

        return proto;
    }

    public static SnWalletSubscription FromProtoValue(DySubscription proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        BegunAt = proto.BegunAt.ToInstant(),
        EndedAt = proto.EndedAt?.ToInstant(),
        Identifier = proto.Identifier,
        IsActive = proto.IsActive,
        IsFreeTrial = proto.IsFreeTrial,
        Status = (SubscriptionStatus)proto.Status,
        PaymentMethod = proto.PaymentMethod,
        PaymentDetails = SnPaymentDetails.FromProtoValue(proto.PaymentDetails),
        BasePrice = decimal.Parse(proto.BasePrice),
        CouponId = proto.HasCouponId ? Guid.Parse(proto.CouponId) : null,
        Coupon = proto.Coupon is not null ? SnWalletCoupon.FromProtoValue(proto.Coupon) : null,
        RenewalAt = proto.RenewalAt?.ToInstant(),
        AccountId = Guid.Parse(proto.AccountId),
        CreatedAt = proto.CreatedAt.ToInstant(),
        UpdatedAt = proto.UpdatedAt.ToInstant()
    };
}

/// <summary>
/// A reference object for Subscription that contains only non-sensitive information
/// suitable for client-side use.
/// </summary>
public class SnSubscriptionReferenceObject : ModelBase
{
    public Guid Id { get; set; }
    public string Identifier { get; set; } = null!;
    public Instant BegunAt { get; set; }
    public Instant? EndedAt { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsFreeTrial { get; set; }
    public SubscriptionStatus Status { get; set; }
    public decimal BasePrice { get; set; }
    public decimal FinalPrice { get; set; }
    public Instant? RenewalAt { get; set; }
    public Guid AccountId { get; set; }

    /// <summary>
    /// Gets the human-readable name of the subscription type if available (cached for performance).
    /// </summary>
    [NotMapped]
    public string? DisplayName => field ??= SubscriptionTypeData.SubscriptionHumanReadable.GetValueOrDefault(Identifier);

    public DySubscriptionReferenceObject ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Identifier = Identifier,
        BegunAt = BegunAt.ToTimestamp(),
        EndedAt = EndedAt?.ToTimestamp(),
        IsActive = IsActive,
        IsAvailable = IsAvailable,
        IsFreeTrial = IsFreeTrial,
        Status = (DySubscriptionStatus)Status,
        BasePrice = BasePrice.ToString(CultureInfo.InvariantCulture),
        FinalPrice = FinalPrice.ToString(CultureInfo.InvariantCulture),
        RenewalAt = RenewalAt?.ToTimestamp(),
        AccountId = AccountId.ToString(),
        DisplayName = DisplayName,
        CreatedAt = CreatedAt.ToTimestamp(),
        UpdatedAt = UpdatedAt.ToTimestamp()
    };

    public static SnSubscriptionReferenceObject FromProtoValue(DySubscriptionReferenceObject proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Identifier = proto.Identifier,
        BegunAt = proto.BegunAt.ToInstant(),
        EndedAt = proto.EndedAt?.ToInstant(),
        IsActive = proto.IsActive,
        IsAvailable = proto.IsAvailable,
        IsFreeTrial = proto.IsFreeTrial,
        Status = (SubscriptionStatus)proto.Status,
        BasePrice = decimal.Parse(proto.BasePrice),
        FinalPrice = decimal.Parse(proto.FinalPrice),
        RenewalAt = proto.RenewalAt?.ToInstant(),
        AccountId = Guid.Parse(proto.AccountId),
        CreatedAt = proto.CreatedAt.ToInstant(),
        UpdatedAt = proto.UpdatedAt.ToInstant()
    };
}

public class SnPaymentDetails
{
    public string Currency { get; set; } = null!;
    public string? OrderId { get; set; }

    public DyPaymentDetails ToProtoValue()
    {
        var proto = new DyPaymentDetails
        {
            Currency = Currency
        };

        if (OrderId != null)
        {
            proto.OrderId = OrderId;
        }

        return proto;
    }

    public static SnPaymentDetails FromProtoValue(DyPaymentDetails proto) => new()
    {
        Currency = proto.Currency,
        OrderId = proto.OrderId,
    };
}

/// <summary>
/// A discount that can applies in purchases among the Solar Network.
/// For now, it can be used in the subscription purchase.
/// </summary>
public class SnWalletCoupon : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The items that can apply this coupon.
    /// Leave it to null to apply to all items.
    /// </summary>
    [MaxLength(4096)]
    public string? Identifier { get; set; }

    /// <summary>
    /// The code that human-readable and memorizable.
    /// Leave it blank to use it only with the ID.
    /// </summary>
    [MaxLength(1024)]
    public string? Code { get; set; }

    public Instant? AffectedAt { get; set; }
    public Instant? ExpiredAt { get; set; }

    /// <summary>
    /// The amount of the discount.
    /// If this field and the rate field are both not null,
    /// the amount discount will be applied and the discount rate will be ignored.
    /// Formula: <code>final price = base price - discount amount</code>
    /// </summary>
    public decimal? DiscountAmount { get; set; }

    /// <summary>
    /// The percentage of the discount.
    /// If this field and the amount field are both not null,
    /// this field will be ignored.
    /// Formula: <code>final price = base price * (1 - discount rate)</code>
    /// </summary>
    public double? DiscountRate { get; set; }

    /// <summary>
    /// The max usage of the current coupon.
    /// Leave it to null to use it unlimited.
    /// </summary>
    public int? MaxUsage { get; set; }

    public DyCoupon ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Identifier = Identifier,
        Code = Code,
        AffectedAt = AffectedAt?.ToTimestamp(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        DiscountAmount = DiscountAmount?.ToString(),
        DiscountRate = DiscountRate,
        MaxUsage = MaxUsage,
        CreatedAt = CreatedAt.ToTimestamp(),
        UpdatedAt = UpdatedAt.ToTimestamp()
    };

    public static SnWalletCoupon FromProtoValue(DyCoupon proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Identifier = proto.Identifier,
        Code = proto.Code,
        AffectedAt = proto.AffectedAt?.ToInstant(),
        ExpiredAt = proto.ExpiredAt?.ToInstant(),
        DiscountAmount = proto.HasDiscountAmount ? decimal.Parse(proto.DiscountAmount) : null,
        DiscountRate = proto.DiscountRate,
        MaxUsage = proto.MaxUsage,
        CreatedAt = proto.CreatedAt.ToInstant(),
        UpdatedAt = proto.UpdatedAt.ToInstant()
    };
}
