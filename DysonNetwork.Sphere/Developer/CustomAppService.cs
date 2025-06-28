using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Developer;

public class CustomAppService(AppDatabase db, PublisherService ps)
{
    public async Task<CustomApp?> CreateAppAsync(Guid publisherId, string name, string slug)
    {
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Id == publisherId);
        if (publisher == null)
        {
            return null;
        }

        if (!await ps.HasFeature(publisherId, "developer"))
        {
            return null;
        }

        var app = new CustomApp
        {
            Name = name,
            Slug = slug,
            PublisherId = publisher.Id
        };

        db.CustomApps.Add(app);
        await db.SaveChangesAsync();

        return app;
    }

    public async Task<CustomApp?> GetAppAsync(Guid id)
    {
        return await db.CustomApps.FindAsync(id);
    }

    public async Task<List<CustomApp>> GetAppsByPublisherAsync(Guid publisherId)
    {
        return await db.CustomApps.Where(a => a.PublisherId == publisherId).ToListAsync();
    }

    public async Task<CustomApp?> UpdateAppAsync(Guid id, string name, string slug)
    {
        var app = await db.CustomApps.FindAsync(id);
        if (app == null)
        {
            return null;
        }

        app.Name = name;
        app.Slug = slug;

        await db.SaveChangesAsync();

        return app;
    }

    public async Task<bool> DeleteAppAsync(Guid id)
    {
        var app = await db.CustomApps.FindAsync(id);
        if (app == null)
        {
            return false;
        }

        db.CustomApps.Remove(app);
        await db.SaveChangesAsync();

        return true;
    }
}