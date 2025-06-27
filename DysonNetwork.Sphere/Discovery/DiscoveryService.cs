using Microsoft.EntityFrameworkCore;
namespace DysonNetwork.Sphere.Discovery;

public class DiscoveryService(AppDatabase appDatabase)
{
    public Task<List<Realm.Realm>> GetPublicRealmsAsync(string? query,
        List<string>? tags,
        int take = 10,
        int offset = 0,
        bool randomizer = false
    )
    {
        var realmsQuery = appDatabase.Realms
            .Take(take)
            .Skip(offset)
            .Where(r => r.IsPublic);

        if (!string.IsNullOrEmpty(query))
        {
            realmsQuery = realmsQuery.Where(r => r.Name.Contains(query) || r.Description.Contains(query));
        }

        if (tags is { Count: > 0 })
            realmsQuery = realmsQuery.Where(r => r.RealmTags.Any(rt => tags.Contains(rt.Tag.Name)));
        if (randomizer)
            realmsQuery = realmsQuery.OrderBy(r => EF.Functions.Random());
        else
            realmsQuery = realmsQuery.OrderByDescending(r => r.CreatedAt);

        return realmsQuery.Skip(offset).Take(take).ToListAsync();
    }
}
