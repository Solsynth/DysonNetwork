using DysonNetwork.Shared.Registry;

namespace DysonNetwork.Sphere.Discovery;

public class DiscoveryService(RemoteRealmService remoteRealmService)
{
    public async Task<List<Shared.Models.SnRealm>> GetCommunityRealmAsync(
        string? query,
        int take = 10,
        int offset = 0,
        bool randomizer = false
    )
    {
        var allRealms = await remoteRealmService.GetPublicRealms();
        var communityRealms = allRealms.Where(r => r.IsCommunity);

        if (!string.IsNullOrEmpty(query))
        {
            communityRealms = communityRealms.Where(r =>
                r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            );
        }

        // Since we don't have CreatedAt in the proto model, we'll just apply randomizer if requested
        var orderedRealms = randomizer
            ? communityRealms.OrderBy(_ => Random.Shared.Next())
            : communityRealms.OrderByDescending(q => q.Members.Count());

        return orderedRealms.Skip(offset).Take(take).ToList();
    }
}
