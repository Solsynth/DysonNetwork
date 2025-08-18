using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class CustomAppService(
    AppDatabase db,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    FileService.FileServiceClient files
)
{
    public async Task<CustomApp?> CreateAppAsync(
        Guid projectId,
        CustomAppController.CustomAppRequest request
    )
    {
        var project = await db.DevProjects
            .Include(p => p.Developer)
            .FirstOrDefaultAsync(p => p.Id == projectId);
            
        if (project == null)
            return null;
            
        var app = new CustomApp
        {
            Slug = request.Slug!,
            Name = request.Name!,
            Description = request.Description,
            Status = request.Status ?? CustomAppStatus.Developing,
            Links = request.Links,
            OauthConfig = request.OauthConfig,
            ProjectId = projectId
        };

        if (request.PictureId is not null)
        {
            var picture = await files.GetFileAsync(
                new GetFileRequest
                {
                    Id = request.PictureId
                }
            );
            if (picture is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Picture = CloudFileReferenceObject.FromProtoValue(picture);

            // Create a new reference
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = picture.Id,
                    Usage = "custom-apps.picture",
                    ResourceId = app.ResourceIdentifier
                }
            );
        }
        if (request.BackgroundId is not null)
        {
            var background = await files.GetFileAsync(
                new GetFileRequest { Id = request.BackgroundId }
            );
            if (background is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Background = CloudFileReferenceObject.FromProtoValue(background);

            // Create a new reference
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = background.Id,
                    Usage = "custom-apps.background",
                    ResourceId = app.ResourceIdentifier
                }
            );
        }

        db.CustomApps.Add(app);
        await db.SaveChangesAsync();

        return app;
    }

    public async Task<CustomApp?> GetAppAsync(Guid id, Guid? projectId = null)
    {
        var query = db.CustomApps.AsQueryable();
        
        if (projectId.HasValue)
        {
            query = query.Where(a => a.ProjectId == projectId.Value);
        }

        return await query.FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<CustomApp>> GetAppsByProjectAsync(Guid projectId)
    {
        return await db.CustomApps
            .Where(a => a.ProjectId == projectId)
            .ToListAsync();
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
            var picture = await files.GetFileAsync(
                new GetFileRequest
                {
                    Id = request.PictureId
                }
            );
            if (picture is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Picture = CloudFileReferenceObject.FromProtoValue(picture);

            // Create a new reference
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = picture.Id,
                    Usage = "custom-apps.picture",
                    ResourceId = app.ResourceIdentifier
                }
            );
        }
        if (request.BackgroundId is not null)
        {
            var background = await files.GetFileAsync(
                new GetFileRequest { Id = request.BackgroundId }
            );
            if (background is null)
                throw new InvalidOperationException("Invalid picture id, unable to find the file on cloud.");
            app.Background = CloudFileReferenceObject.FromProtoValue(background);

            // Create a new reference
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    FileId = background.Id,
                    Usage = "custom-apps.background",
                    ResourceId = app.ResourceIdentifier
                }
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

        await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
            {
                ResourceId = app.ResourceIdentifier
            }
        );

        return true;
    }
}