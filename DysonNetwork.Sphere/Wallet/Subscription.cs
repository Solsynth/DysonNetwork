using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Wallet;

public record class SubscriptionTypeData(
    string Identifier,
    decimal BasePrice
)
{
    public static Dictionary<string, SubscriptionTypeData> SubscriptionDict =
        new()
        {
            [SubscriptionType.Twinkle] = new SubscriptionTypeData(SubscriptionType.Twinkle, 0),
            [SubscriptionType.Stellar] = new SubscriptionTypeData(SubscriptionType.Stellar, 10),
            [SubscriptionType.Nova] = new SubscriptionTypeData(SubscriptionType.Nova, 20),
            [SubscriptionType.Supernova] = new SubscriptionTypeData(SubscriptionType.Supernova, 30)
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
    Paid,
    Expired,
    Cancelled
}

/// <summary>
/// The subscription is for the Stellar Program in most cases.
/// The paid subscription in another word.
/// </summary>
[Index(nameof(Identifier))]
public class Subscription : ModelBase
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
    [Column(TypeName = "jsonb")] public PaymentDetails PaymentDetails { get; set; } = null!;
    public decimal BasePrice { get; set; }
    public Guid? CouponId { get; set; }
    public Coupon? Coupon { get; set; }
    public Instant? RenewalAt { get; set; }

    public Guid AccountId { get; set; }
    public Account.Account Account { get; set; } = null!;

    [NotMapped]
    public bool IsAvailable
    {
        get
        {
            if (!IsActive) return false;

            var now = SystemClock.Instance.GetCurrentInstant();

            if (BegunAt > now) return false;
            if (EndedAt.HasValue && now > EndedAt.Value) return false;
            if (RenewalAt.HasValue && now > RenewalAt.Value) return false;
            if (Status != SubscriptionStatus.Paid) return false;

            return true;
        }
    }

    [NotMapped]
    public decimal FinalPrice
    {
        get
        {
            if (IsFreeTrial) return 0;
            if (Coupon == null) return BasePrice;

            var now = SystemClock.Instance.GetCurrentInstant();
            if (Coupon.AffectedAt.HasValue && now < Coupon.AffectedAt.Value ||
                Coupon.ExpiredAt.HasValue && now > Coupon.ExpiredAt.Value) return BasePrice;

            if (Coupon.DiscountAmount.HasValue) return BasePrice - Coupon.DiscountAmount.Value;
            if (Coupon.DiscountRate.HasValue) return BasePrice * (decimal)(1 - Coupon.DiscountRate.Value);
            return BasePrice;
        }
    }

    public SubscriptionReferenceObject ToReference()
    {
        return new SubscriptionReferenceObject
        {
            Id = Id,
            BegunAt = BegunAt,
            EndedAt = EndedAt,
            Identifier = Identifier,
            IsActive = IsActive,
            AccountId = AccountId,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            DeletedAt = DeletedAt,
        };
    }
}

public class PaymentDetails
{
    public string Currency { get; set; } = null!;
    public string? OrderId { get; set; }
}

public class SubscriptionReferenceObject : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Instant BegunAt { get; set; }
    public Instant? EndedAt { get; set; }
    [MaxLength(4096)] public string Identifier { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public Guid AccountId { get; set; }
}

/// <summary>
/// A discount that can applies in purchases among the Solar Network.
/// For now, it can be used in the subscription purchase.
/// </summary>
public class Coupon : ModelBase
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
}