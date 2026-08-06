using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime;
using Xunit;

namespace DysonNetwork.Shared.Tests.Models;

public class AccountProtoMappingTests
{
    private const string AccountId = "11111111-1111-1111-1111-111111111111";

    // The wallet returns DySubscriptionReferenceObject with an empty Id as the
    // "no active perk subscription" sentinel. FromProtoValue must map it to
    // null — Guid.Parse("") throws FormatException and previously failed auth
    // for every user without a subscription.
    [Fact]
    public void FromProtoValue_MapsEmptyPerkSubscriptionToNull()
    {
        var account = SnAccount.FromProtoValue(new DyAccount
        {
            Id = AccountId,
            Name = "tester",
            Nick = "tester",
            PerkSubscription = new DySubscriptionReferenceObject
            {
                Id = "",
                Identifier = "solian.stellar",
            }
        });

        Assert.Null(account.PerkSubscription);
    }

    [Fact]
    public void FromProtoValue_MapsValidPerkSubscription()
    {
        var begunAt = Timestamp.FromDateTime(
            DateTime.SpecifyKind(new DateTime(2025, 11, 8, 5, 35, 47), DateTimeKind.Utc)
        );
        var account = SnAccount.FromProtoValue(new DyAccount
        {
            Id = AccountId,
            Name = "tester",
            Nick = "tester",
            PerkSubscription = new DySubscriptionReferenceObject
            {
                Id = "9297d27f-9acd-4308-bea2-d6b692389c8f",
                Identifier = "solian.stellar",
                BegunAt = begunAt,
                CreatedAt = begunAt,
                UpdatedAt = begunAt,
                BasePrice = "19.99",
                FinalPrice = "19.99",
                AccountId = AccountId,
            }
        });

        Assert.NotNull(account.PerkSubscription);
        Assert.Equal(
            Guid.Parse("9297d27f-9acd-4308-bea2-d6b692389c8f"),
            account.PerkSubscription.Id
        );
    }

    // A subscription reference with a valid Id but missing timestamps and
    // prices (the wallet can emit null BegunAt/CreatedAt/UpdatedAt for DB
    // rows) must map without throwing: previously ToInstant(null) raised
    // ArgumentNullException and failed auth.
    [Fact]
    public void FromProtoValue_SparsePerkSubscriptionDoesNotThrow()
    {
        var account = SnAccount.FromProtoValue(new DyAccount
        {
            Id = AccountId,
            Name = "tester",
            Nick = "tester",
            PerkSubscription = new DySubscriptionReferenceObject
            {
                Id = "9297d27f-9acd-4308-bea2-d6b692389c8f",
                Identifier = "solian.stellar",
            }
        });

        Assert.NotNull(account.PerkSubscription);
        Assert.Equal(0m, account.PerkSubscription.BasePrice);
        Assert.Equal(0m, account.PerkSubscription.FinalPrice);
        Assert.Equal(default, account.PerkSubscription.BegunAt);
        Assert.Equal(Guid.Empty, account.PerkSubscription.AccountId);
    }

    // Malformed (non-empty, non-GUID) ids must degrade to Guid.Empty instead
    // of throwing FormatException.
    [Fact]
    public void FromProtoValue_MalformedIdsDoNotThrow()
    {
        var account = SnAccount.FromProtoValue(new DyAccount
        {
            Id = "not-a-guid",
            Name = "tester",
            Nick = "tester",
            PerkSubscription = new DySubscriptionReferenceObject
            {
                Id = "also-not-a-guid",
                Identifier = "solian.stellar",
            }
        });

        Assert.Equal(Guid.Empty, account.Id);
        Assert.NotNull(account.PerkSubscription);
        Assert.Equal(Guid.Empty, account.PerkSubscription.Id);
    }
}
