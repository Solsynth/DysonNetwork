using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Xunit;

namespace DysonNetwork.Padlock.Tests.Auth;

public class AuthSessionCacheSerializationTests
{
    [Fact]
    public void JsonCacheSerializer_PreservesOAuthPermissionScopesInSessionCacheModel()
    {
        var session = new DyAuthSession
        {
            Id = Guid.NewGuid().ToString(),
            AccountId = Guid.NewGuid().ToString(),
            Type = DySessionType.DyOauth,
            Account = new DyAccount { Id = Guid.NewGuid().ToString() }
        };
        session.Scopes.Add("openid");
        session.Scopes.Add("chat.messages.create");

        var cacheModel = SnAuthSession.FromProtoValue(session);
        var serializer = new JsonCacheSerializer();
        var restored = serializer.Deserialize<SnAuthSession>(serializer.Serialize(cacheModel));

        Assert.NotNull(restored);
        Assert.Contains("chat.messages.create", restored!.Scopes);
        Assert.Contains("chat.messages.create", restored.ToProtoValue().Scopes);
    }
}
