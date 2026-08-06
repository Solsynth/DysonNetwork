using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Xunit;

namespace DysonNetwork.Shared.Tests.Models;

public class SubscriptionProtoMappingTests
{
    // The wallet family of models is fed from the same gRPC surface as the
    // auth path: every parse must tolerate absent/malformed fields instead of
    // throwing.
    [Fact]
    public void SnWalletSubscription_SparseProtoDoesNotThrow()
    {
        var subscription = SnWalletSubscription.FromProtoValue(new DySubscription
        {
            Id = "9297d27f-9acd-4308-bea2-d6b692389c8f",
            Identifier = "solian.stellar",
        });

        Assert.Equal(0m, subscription.BasePrice);
        Assert.Equal(default, subscription.BegunAt);
        Assert.Equal(Guid.Empty, subscription.AccountId);
    }

    [Fact]
    public void SnWalletGift_SparseProtoDoesNotThrow()
    {
        var gift = SnWalletGift.FromProtoValue(new DyGift
        {
            Id = "9297d27f-9acd-4308-bea2-d6b692389c8f",
            GiftCode = "GIFT-1",
        });

        Assert.Equal(Guid.Empty, gift.GifterId);
        Assert.Equal(0m, gift.BasePrice);
        Assert.Equal(default, gift.ExpiresAt);
    }

    [Fact]
    public void SnWalletCoupon_SparseProtoDoesNotThrow()
    {
        var coupon = SnWalletCoupon.FromProtoValue(new DyCoupon
        {
            Id = "9297d27f-9acd-4308-bea2-d6b692389c8f",
            Code = "WELCOME",
        });

        Assert.Equal(Guid.Parse("9297d27f-9acd-4308-bea2-d6b692389c8f"), coupon.Id);
        Assert.Equal(default, coupon.CreatedAt);
        Assert.Null(coupon.DiscountAmount);
    }

    [Fact]
    public void SnAuthSession_SparseProtoDoesNotThrow()
    {
        var session = SnAuthSession.FromProtoValue(new DyAuthSession
        {
            Id = "",
            AccountId = "",
        });

        Assert.Equal(Guid.Empty, session.Id);
        Assert.Equal(Guid.Empty, session.AccountId);
    }
}
