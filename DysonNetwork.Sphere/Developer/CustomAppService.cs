using DysonNetwork.Sphere.Publisher;
using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Developer;

public class CustomAppService(AppDatabase db, FileReferenceService fileRefService)
{
    public async Task<CustomApp?> CreateAppAsync(
        Publisher.Publisher pub,
        CustomAppController.CustomAppRequest request
    )
    {
        var app = new CustomApp
        {
            Slug = request.Slug!,
            Name = request.Name!,
            Description = request.Description,
            Status = request.Status ?? CustomAppStatus.Developing,
            Links = request.Links,
            OauthConfig = request.OauthConfig,
            PublisherId = pub.Id
        };
        
        if (request.PictureId is not null)
        {
            var picture = await db.Files.Where(f => f.Id == request.PictureId).FirstOrDefaultAsync();
            if (picture is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");

            app.Picture = picture.ToReferenceObject();

            // Create a new reference
            await fileRefService.CreateReferenceAsync(
                picture.Id,
                "custom-apps.picture",
                app.ResourceIdentifier 
            );
        }
        
        if (request.BackgroundId is not null)
        {
            var background = await db.Files.Where(f => f.Id == request.BackgroundId).FirstOrDefaultAsync();
            if (background is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");

            app.Background = background.ToReferenceObject();

            // Create a new reference
            await fileRefService.CreateReferenceAsync(
                background.Id,
                "custom-apps.background",
                app.ResourceIdentifier 
            );
        }

        db.CustomApps.Add(app);
        await db.SaveChangesAsync();

        return app;
    }

    public async Task<CustomApp?> GetAppAsync(Guid id, Guid? publisherId = null)
    {
        var query = db.CustomApps.Where(a => a.Id == id).AsQueryable();
        if (publisherId.HasValue)
            query = query.Where(a => a.PublisherId == publisherId.Value);
        return await query.FirstOrDefaultAsync();
    }

    public async Task<List<CustomApp>> GetAppsByPublisherAsync(Guid publisherId)
    {
        return await db.CustomApps.Where(a => a.PublisherId == publisherId).ToListAsync();
    }

    public async Task<CustomApp?> UpdateAppAsync(CustomApp app, CustomAppController.CustomAppRequest request)
    {
        if (request.Slug is not null)
            app.Slug = request.Slug;
        if (request.Name is not null)
            app.Name = request.Name;
        if (request.Description is not null)
            app.Description = request.Description;
        if (request.Status is not null)
            app.Status = request.Status.Value;
        if (request.Links is not null)
            app.Links = request.Links;
        if (request.OauthConfig is not null)
            app.OauthConfig = request.OauthConfig;

        if (request.PictureId is not null)
        {
            var picture = await db.Files.Where(f => f.Id == request.PictureId).FirstOrDefaultAsync();
            if (picture is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");

            if (app.Picture is not null)
                await fileRefService.DeleteResourceReferencesAsync(app.ResourceIdentifier, "custom-apps.picture");

            app.Picture = picture.ToReferenceObject();

            // Create a new reference
            await fileRefService.CreateReferenceAsync(
                picture.Id,
                "custom-apps.picture",
               app.ResourceIdentifier 
            );
        }
        
        if (request.BackgroundId is not null)
        {
            var background = await db.Files.Where(f => f.Id == request.BackgroundId).FirstOrDefaultAsync();
            if (background is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");

            if (app.Background is not null)
                await fileRefService.DeleteResourceReferencesAsync(app.ResourceIdentifier, "custom-apps.background");

            app.Background = background.ToReferenceObject();

            // Create a new reference
            await fileRefService.CreateReferenceAsync(
                background.Id,
                "custom-apps.background",
                app.ResourceIdentifier 
            );
        }

        db.Update(app);
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
        
        await fileRefService.DeleteResourceReferencesAsync(app.ResourceIdentifier);

        return true;
    }
}