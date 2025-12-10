using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Sphere.Discovery;

public class DiscoveryService(RemoteRealmService remoteRealmService)
{
    public async Task<List<Shared.Models.SnRealm>> GetCommunityRealmAsync(
        string? query,
        int take = 10,
        int offset = 0,
        bool random = false
    )
    {
        var allRealms = await remoteRealmService.GetPublicRealms(random ? "random" : "popularity");
        return allRealms.Where(r => r.IsCommunity).Skip(offset).Take(take).ToList();
    }
}