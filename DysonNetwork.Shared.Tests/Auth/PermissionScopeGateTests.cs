using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Xunit;

namespace DysonNetwork.Shared.Tests.Auth;

public class PermissionScopeGateTests
{
    // Login sessions are issued carrying the full-grant wildcard (Stargate
    // scopesWithFullScope). Honouring it for anything but OAuth sessions
    // skipped the scope check, the superuser check and the permission-service
    // call for every ordinary account.
    [Theory]
    [InlineData(DySessionType.DyLogin)]
    [InlineData(DySessionType.DyOidc)]
    [InlineData(DySessionType.DyApiKey)]
    public void HasFullScope_IgnoresWildcardOnNonOAuthProtoSessions(DySessionType type)
    {
        var session = new DyAuthSession { Type = type };
        session.Scopes.Add("*");

        Assert.False(PermissionScopeGate.HasFullScope(session));
    }

    [Fact]
    public void HasFullScope_HonoursWildcardOnOAuthProtoSessions()
    {
        var session = new DyAuthSession { Type = DySessionType.DyOauth };
        session.Scopes.Add("*");

        Assert.True(PermissionScopeGate.HasFullScope(session));
    }

    [Theory]
    [InlineData(SessionType.Login)]
    [InlineData(SessionType.Oidc)]
    [InlineData(SessionType.ApiKey)]
    public void HasFullScope_IgnoresWildcardOnNonOAuthModelSessions(SessionType type)
    {
        var session = new SnAuthSession { Type = type, Scopes = ["*"] };

        Assert.False(PermissionScopeGate.HasFullScope(session));
    }

    [Fact]
    public void HasFullScope_HonoursWildcardOnOAuthModelSessions()
    {
        var session = new SnAuthSession { Type = SessionType.OAuth, Scopes = ["*"] };

        Assert.True(PermissionScopeGate.HasFullScope(session));
    }

    [Fact]
    public void HasFullScope_RejectsMissingSessionsAndAbsentWildcard()
    {
        Assert.False(PermissionScopeGate.HasFullScope((DyAuthSession?)null));
        Assert.False(PermissionScopeGate.HasFullScope((SnAuthSession?)null));
        Assert.False(PermissionScopeGate.HasFullScope(new DyAuthSession { Type = DySessionType.DyOauth }));
        Assert.False(PermissionScopeGate.HasFullScope(new SnAuthSession { Type = SessionType.OAuth }));
    }
}
