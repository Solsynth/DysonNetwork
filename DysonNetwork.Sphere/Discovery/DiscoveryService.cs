using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DysonNetwork.Sphere;
using DysonNetwork.Sphere.Realm;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Discovery;

public class DiscoveryService(AppDatabase appDatabase)
{
    public Task<List<Realm.Realm>> GetPublicRealmsAsync(string? query, List<string>? tags)
    {
        var realmsQuery = appDatabase.Realms
            .Where(r => r.IsPublic);

        if (!string.IsNullOrEmpty(query))
        {
            realmsQuery = realmsQuery.Where(r => r.Name.Contains(query) || r.Description.Contains(query));
        }

        if (tags != null && tags.Count > 0)
        {
            realmsQuery = realmsQuery.Where(r => r.RealmTags.Any(rt => tags.Contains(rt.Tag.Name)));
        }

        return realmsQuery.ToListAsync();
    }
}
