using DysonNetwork.Shared.Auth;
using Xunit;

namespace DysonNetwork.Shared.Tests.Auth;

public class PermissionKeysWalletAdminTests
{
    public static TheoryData<string> AdminWalletKeys = new()
    {
        PermissionKeys.AdminWalletsOrdersView,
        PermissionKeys.AdminWalletsOrdersManage,
        PermissionKeys.AdminWalletsProductsView,
    };

    // The wallet admin listings and the golds resupply order apply used to be
    // gated on orders.view / orders.pay, which the `default` permission group
    // grants every account: any logged-in account could read the full-station
    // payment listings and apply paid wallet product orders.
    [Fact]
    public void AdminWalletKeys_HaveExpectedWireValues()
    {
        Assert.Equal("admin.wallets.orders.view", PermissionKeys.AdminWalletsOrdersView);
        Assert.Equal("admin.wallets.orders.manage", PermissionKeys.AdminWalletsOrdersManage);
        Assert.Equal("admin.wallets.products.view", PermissionKeys.AdminWalletsProductsView);
    }

    [Theory]
    [MemberData(nameof(AdminWalletKeys))]
    public void AdminWalletKeys_AreNotGrantedToTheDefaultGroup(string key)
    {
        Assert.StartsWith("admin.", key);
        Assert.DoesNotContain(key, DefaultGrantedPermissionKeys);
    }

    // Directly pins the keys the wallet admin routes were re-pointed away from.
    [Theory]
    [MemberData(nameof(AdminWalletKeys))]
    public void AdminWalletKeys_DoNotReuseTheUserScopedOrderKeys(string key)
    {
        Assert.NotEqual(PermissionKeys.OrdersView, key);
        Assert.NotEqual(PermissionKeys.OrdersPay, key);
    }

    // Mirror of Stargate's permission.DefaultPermissionKeys()
    // (Stargate/internal/permission/service.go): the keys the `default`
    // permission group grants every account. Stargate owns the authoritative
    // route-level assertion (internal/httpserver/adminctl/adminctl_permission_test.go);
    // this copy exists so re-pointing a wallet admin route at a user-scoped key
    // fails in the C# suite as well.
    private static readonly HashSet<string> DefaultGrantedPermissionKeys =
    [
        "tests.take", "account.connections", "chat.create", "chat.update",
        "chat.delete", "chat.messages.create", "chat.messages.update", "chat.messages.delete",
        "chat.messages.react", "chat.members.manage", "chat.members.timeout", "chat.members.kick",
        "chat.invites.manage", "chat.e2ee.manage", "chat.sync", "chat.call.start",
        "chat.call.end", "chat.call.invite", "chat.call.kick", "chat.call.mute",
        "chat.groups.manage", "chat.pins.manage", "notifications.put", "notifications.read.all",
        "notifications.preferences.manage", "notifications.subscriptions.manage", "wallets.create", "orders.create",
        "orders.update", "orders.pay", "orders.view", "subscriptions.create",
        "subscriptions.cancel", "subscriptions.checkout", "subscription.gifts.purchase", "subscription.gifts.redeem",
        "subscription.gifts.send", "subscription.gifts.cancel", "auth.sessions.manage", "auth.factors.manage",
        "auth.api.keys.manage", "auth.apps.authorize", "auth.recover", "account.contacts.manage",
        "account.devices.manage", "account.authorized.apps.manage", "e2ee.keys.manage", "e2ee.mls.manage",
        "e2ee.devices.manage", "chat.read.all", "accounts.statuses.create", "accounts.statuses.update",
        "nfc.tags.create", "nfc.tags.update", "nfc.tags.delete", "nfc.tags.claim",
        "nfc.tags.lock", "calendar.events.create", "calendar.events.update", "calendar.events.delete",
        "calendar.subscriptions.manage", "calendar.checkin.manage", "stickers.packs.create", "stickers.packs.update",
        "stickers.packs.delete", "stickers.packs.own", "stickers.packs.order", "stickers.create",
        "stickers.update", "stickers.delete", "stickers.content.update", "surveys.create",
        "surveys.update", "surveys.delete", "surveys.publish", "surveys.archive",
        "surveys.clone", "notable.days.create", "notable.days.update", "notable.days.delete",
        "tickets.create", "progression.badges.manage", "files.upload",
    ];
}
