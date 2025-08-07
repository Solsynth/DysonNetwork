using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Discovery;

public class DiscoveryService(AppDatabase appDatabase)
{
    public Task<List<Realm.Realm>> GetPublicRealmsAsync(
        string? query,
        int take = 10,
        int offset = 0,
        bool randomizer = false
    )
    {
        var realmsQuery = appDatabase.Realms
            .Take(take)
            .Skip(offset)
            .Where(r => r.IsCommunity);

        if (!string.IsNullOrEmpty(query))
            realmsQuery = realmsQuery.Where(r =>
                EF.Functions.ILike(r.Name, $"%{query}%") ||
                EF.Functions.ILike(r.Description, $"%{query}%")
            );
        if (randomizer)
            realmsQuery = realmsQuery.OrderBy(r => EF.Functions.Random());
        else
            realmsQuery = realmsQuery.OrderByDescending(r => r.CreatedAt);

        return realmsQuery.Skip(offset).Take(take).ToListAsync();
    }
}