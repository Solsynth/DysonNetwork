using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Globalization;
using DysonNetwork.Shared.Data;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Wallet;

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
                3
            ),
            [SubscriptionType.Nova] = new SubscriptionTypeData(
                SubscriptionType.Nova,
                SubscriptionType.StellarProgram,
                WalletCurrency.SourcePoint,
                2400,
                6
            ),
            [SubscriptionType.Supernova] = new SubscriptionTypeData(
                SubscriptionType.Supernova,
                SubscriptionType.StellarProgram,
                WalletCurrency.SourcePoint,
                3600,
                9
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
            if (Status != SubscriptionStatus.Active) return false;

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

    /// <summary>
    /// Returns a reference object that contains a subset of subscription data
    /// suitable for client-side use, with sensitive information removed.
    /// </summary>
    public SubscriptionReferenceObject ToReference()
    {
        return new SubscriptionReferenceObject
        {
            Id = Id,
            Identifier = Identifier,
            BegunAt = BegunAt,
            EndedAt = EndedAt,
            IsActive = IsActive,
            IsAvailable = IsAvailable,
            IsFreeTrial = IsFreeTrial,
            Status = Status,
            BasePrice = BasePrice,
            FinalPrice = FinalPrice,
            RenewalAt = RenewalAt,
            AccountId = AccountId
        };
    }

    public Shared.Proto.Subscription ToProtoValue() => new()
    {
        Id = Id.ToString(),
        BegunAt = BegunAt.ToTimestamp(),
        EndedAt = EndedAt?.ToTimestamp(),
        Identifier = Identifier,
        IsActive = IsActive,
        IsFreeTrial = IsFreeTrial,
        Status = (Shared.Proto.SubscriptionStatus)Status,
        PaymentMethod = PaymentMethod,
        PaymentDetails = PaymentDetails.ToProtoValue(),
        BasePrice = BasePrice.ToString(),
        CouponId = CouponId?.ToString(),
        Coupon = Coupon?.ToProtoValue(),
        RenewalAt = RenewalAt?.ToTimestamp(),
        AccountId = AccountId.ToString(),
        IsAvailable = IsAvailable,
        FinalPrice = FinalPrice.ToString(),
    };

    public static Subscription FromProtoValue(Shared.Proto.Subscription proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        BegunAt = proto.BegunAt.ToInstant(),
        EndedAt = proto.EndedAt?.ToInstant(),
        Identifier = proto.Identifier,
        IsActive = proto.IsActive,
        IsFreeTrial = proto.IsFreeTrial,
        Status = (SubscriptionStatus)proto.Status,
        PaymentMethod = proto.PaymentMethod,
        PaymentDetails = PaymentDetails.FromProtoValue(proto.PaymentDetails),
        BasePrice = decimal.Parse(proto.BasePrice),
        CouponId = proto.HasCouponId ? Guid.Parse(proto.CouponId) : null,
        Coupon = proto.Coupon is not null ? Coupon.FromProtoValue(proto.Coupon) : null,
        RenewalAt = proto.RenewalAt?.ToInstant(),
        AccountId = Guid.Parse(proto.AccountId),
    };
}

/// <summary>
/// A reference object for Subscription that contains only non-sensitive information
/// suitable for client-side use.
/// </summary>
public class SubscriptionReferenceObject : ModelBase
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
    /// Gets the human-readable name of the subscription type if available.
    /// </summary>
    [NotMapped]
    public string? DisplayName => SubscriptionTypeData.SubscriptionHumanReadable.TryGetValue(Identifier, out var name) 
        ? name 
        : null;

    public Shared.Proto.SubscriptionReferenceObject ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Identifier = Identifier,
        BegunAt = BegunAt.ToTimestamp(),
        EndedAt = EndedAt?.ToTimestamp(),
        IsActive = IsActive,
        IsAvailable = IsAvailable,
        IsFreeTrial = IsFreeTrial,
        Status = (Shared.Proto.SubscriptionStatus)Status,
        BasePrice = BasePrice.ToString(CultureInfo.CurrentCulture),
        FinalPrice = FinalPrice.ToString(CultureInfo.CurrentCulture),
        RenewalAt = RenewalAt?.ToTimestamp(),
        AccountId = AccountId.ToString(),
        DisplayName = DisplayName,
    };

    public static SubscriptionReferenceObject FromProtoValue(Shared.Proto.SubscriptionReferenceObject proto) => new()
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
    };
}

public class PaymentDetails
{
    public string Currency { get; set; } = null!;
    public string? OrderId { get; set; }

    public Shared.Proto.PaymentDetails ToProtoValue() => new()
    {
        Currency = Currency,
        OrderId = OrderId,
    };

    public static PaymentDetails FromProtoValue(Shared.Proto.PaymentDetails proto) => new()
    {
        Currency = proto.Currency,
        OrderId = proto.OrderId,
    };
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

    public Shared.Proto.Coupon ToProtoValue() => new()
    {
        Id = Id.ToString(),
        Identifier = Identifier,
        Code = Code,
        AffectedAt = AffectedAt?.ToTimestamp(),
        ExpiredAt = ExpiredAt?.ToTimestamp(),
        DiscountAmount = DiscountAmount?.ToString(),
        DiscountRate = DiscountRate,
        MaxUsage = MaxUsage,
    };

    public static Coupon FromProtoValue(Shared.Proto.Coupon proto) => new()
    {
        Id = Guid.Parse(proto.Id),
        Identifier = proto.Identifier,
        Code = proto.Code,
        AffectedAt = proto.AffectedAt?.ToInstant(),
        ExpiredAt = proto.ExpiredAt?.ToInstant(),
        DiscountAmount = proto.HasDiscountAmount ? decimal.Parse(proto.DiscountAmount) : null,
        DiscountRate = proto.DiscountRate,
        MaxUsage = proto.MaxUsage,
    };
}