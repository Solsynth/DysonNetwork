using DysonNetwork.Padlock.Permission;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Xunit;

namespace DysonNetwork.Padlock.Tests.Permission;

public class PermissionBehaviorTests
{
    [Theory]
    [InlineData("chat.*", "chat.messages.send", true)]
    [InlineData("chat.*", "account.profile.read", false)]
    [InlineData("CHAT.*", "chat.messages.send", true)]
    [InlineData("*.read", "chat.messages.read", true)]
    [InlineData("*.read", "chat.messages.write", false)]
    public void MatchesWildcard_UsesCaseInsensitiveGlobMatching(string pattern, string key, bool expected)
    {
        Assert.Equal(expected, PermissionService.MatchesWildcard(pattern, key));
    }

    [Fact]
    public void PermissionCacheKeys_AreSeparatedByActorType()
    {
        var accountKey = PermissionService.GetPermissionCacheKey(
            PermissionNodeActorType.Account,
            "same-actor",
            "chat.messages.send"
        );
        var groupKey = PermissionService.GetPermissionCacheKey(
            PermissionNodeActorType.Group,
            "same-actor",
            "chat.messages.send"
        );

        Assert.NotEqual(accountKey, groupKey);
    }

    [Fact]
    public void BuildBlockedPermissionSet_FlattensNullablePunishmentPermissionLists()
    {
        var blockedPermissions = PermissionService.BuildBlockedPermissionSet(
            [null, ["chat.messages.send", "CHAT.MESSAGES.SEND"], ["account.profile.read"]]
        );

        Assert.Equal(2, blockedPermissions.Count);
        Assert.Contains("chat.messages.send", blockedPermissions);
        Assert.Contains("ACCOUNT.PROFILE.READ", blockedPermissions);
    }

    [Theory]
    [InlineData("chat.messages.send", "chat.messages.send", true)]
    [InlineData("chat.*", "chat.messages.send", true)]
    [InlineData("openid", "chat.messages.send", false)]
    [InlineData("chat.*", "account.profile.read", false)]
    public void PermissionScopeGate_OnlyAcceptsMatchingPermissionScopes(string scope, string permission, bool expected)
    {
        Assert.Equal(expected, PermissionScopeGate.IsPermissionEnabled([scope], permission));
    }
}
