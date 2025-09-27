using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace DysonNetwork.Develop.Identity;

public class CustomAppService(
    AppDatabase db,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    FileService.FileServiceClient files
)
{
    public async Task<SnCustomApp?> CreateAppAsync(
        Guid projectId,
        CustomAppController.CustomAppRequest request
    )
    {
        var project = await db.DevProjects
            .Include(p => p.Developer)
            .FirstOrDefaultAsync(p => p.Id == projectId);
            
        if (project == null)
            return null;
            
        var app = new SnCustomApp
        {
            Slug = request.Slug!,
            Name = request.Name!,
            Description = request.Description,
            Status = request.Status ?? Shared.Models.CustomAppStatus.Developing,
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
            app.Picture = SnCloudFileReferenceObject.FromProtoValue(picture);

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
            app.Background = SnCloudFileReferenceObject.FromProtoValue(background);

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

    public async Task<SnCustomApp?> GetAppAsync(Guid id, Guid? projectId = null)
    {
        var query = db.CustomApps.AsQueryable();
        
        if (projectId.HasValue)
        {
            query = query.Where(a => a.ProjectId == projectId.Value);
        }

        return await query.FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<SnCustomAppSecret>> GetAppSecretsAsync(Guid appId)
    {
        return await db.CustomAppSecrets
            .Where(s => s.AppId == appId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<SnCustomAppSecret?> GetAppSecretAsync(Guid secretId, Guid appId)
    {
        return await db.CustomAppSecrets
            .FirstOrDefaultAsync(s => s.Id == secretId && s.AppId == appId);
    }

    public async Task<SnCustomAppSecret> CreateAppSecretAsync(SnCustomAppSecret secret)
    {
        if (string.IsNullOrWhiteSpace(secret.Secret))
        {
            // Generate a new random secret if not provided
            secret.Secret = GenerateRandomSecret();
        }

        secret.Id = Guid.NewGuid();
        secret.CreatedAt = NodaTime.SystemClock.Instance.GetCurrentInstant();
        secret.UpdatedAt = secret.CreatedAt;

        db.CustomAppSecrets.Add(secret);
        await db.SaveChangesAsync();

        return secret;
    }

    public async Task<bool> DeleteAppSecretAsync(Guid secretId, Guid appId)
    {
        var secret = await db.CustomAppSecrets
            .FirstOrDefaultAsync(s => s.Id == secretId && s.AppId == appId);

        if (secret == null)
            return false;

        db.CustomAppSecrets.Remove(secret);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<SnCustomAppSecret> RotateAppSecretAsync(SnCustomAppSecret secretUpdate)
    {
        var existingSecret = await db.CustomAppSecrets
            .FirstOrDefaultAsync(s => s.Id == secretUpdate.Id && s.AppId == secretUpdate.AppId);

        if (existingSecret == null)
            throw new InvalidOperationException("Secret not found");

        // Update the existing secret with new values
        existingSecret.Secret = GenerateRandomSecret();
        existingSecret.Description = secretUpdate.Description ?? existingSecret.Description;
        existingSecret.ExpiredAt = secretUpdate.ExpiredAt ?? existingSecret.ExpiredAt;
        existingSecret.IsOidc = secretUpdate.IsOidc;
        existingSecret.UpdatedAt = NodaTime.SystemClock.Instance.GetCurrentInstant();

        await db.SaveChangesAsync();
        return existingSecret;
    }

    private static string GenerateRandomSecret(int length = 64)
    {
        const string valid = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890-._~+";
        var res = new StringBuilder();
        using (var rng = RandomNumberGenerator.Create())
        {
            var uintBuffer = new byte[sizeof(uint)];
            while (length-- > 0)
            {
                rng.GetBytes(uintBuffer);
                var num = BitConverter.ToUInt32(uintBuffer, 0);
                res.Append(valid[(int)(num % (uint)valid.Length)]);
            }
        }
        return res.ToString();
    }

    public async Task<List<SnCustomApp>> GetAppsByProjectAsync(Guid projectId)
    {
        return await db.CustomApps
            .Where(a => a.ProjectId == projectId)
            .ToListAsync();
    }

    public async Task<SnCustomApp?> UpdateAppAsync(SnCustomApp app, CustomAppController.CustomAppRequest request)
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
            app.Picture = SnCloudFileReferenceObject.FromProtoValue(picture);

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
            app.Background = SnCloudFileReferenceObject.FromProtoValue(background);

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