using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Discovery;

public class DiscoveryService(AppDatabase appDatabase)
{
    public Task<List<Realm.Realm>> GetCommunityRealmAsync(
        string? query,
        int take = 10,
        int offset = 0,
        bool randomizer = false
    )
    {
        var realmsQuery = appDatabase.Realms
            .Where(r => r.IsCommunity)
            .OrderByDescending(r => r.CreatedAt)
            .AsQueryable();

        if (!string.IsNullOrEmpty(query))
            realmsQuery = realmsQuery.Where(r =>
                EF.Functions.ILike(r.Name, $"%{query}%") ||
                EF.Functions.ILike(r.Description, $"%{query}%")
            );
        realmsQuery = randomizer
            ? realmsQuery.OrderBy(r => EF.Functions.Random())
            : realmsQuery.OrderByDescending(r => r.CreatedAt);

        return realmsQuery.Skip(offset).Take(take).ToListAsync();
    }
}