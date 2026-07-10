using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Develop.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.MiniApp;

public class MiniAppService(AppDatabase db, DyFileService.DyFileServiceClient files)
{
    public async Task<SnMiniApp?> GetMiniAppByIdAsync(Guid id)
    {
        return await db.MiniApps
            .Include(m => m.Project)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<SnMiniApp?> GetMiniAppBySlugAsync(string slug)
    {
        return await db.MiniApps
            .Include(m => m.Project)
            .FirstOrDefaultAsync(m => m.Slug.ToLower() == slug.ToLowerInvariant());
    }

    public async Task<SnMiniApp?> GetPublishedMiniAppBySlugAsync(string slug)
    {
        return await db.MiniApps
            .AsNoTracking()
            .Include(m => m.Project)
                .ThenInclude(p => p.Developer)
            .FirstOrDefaultAsync(m => m.Stage == MiniAppStage.Production &&
                                      m.Slug.ToLower() == slug.ToLowerInvariant());
    }

    public async Task<(List<SnMiniApp> MiniApps, int Total)> GetPublishedMiniAppsAsync(
        int take = 20,
        int offset = 0,
        string? search = null)
    {
        var query = db.MiniApps
            .AsNoTracking()
            .Include(m => m.Project)
                .ThenInclude(p => p.Developer)
            .Where(m => m.Stage == MiniAppStage.Production);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var probe = $"%{search.Trim()}%";
            query = query.Where(m =>
                EF.Functions.ILike(m.Slug, probe) ||
                EF.Functions.ILike(m.PluginId, probe) ||
                EF.Functions.ILike(m.Name, probe) ||
                (m.Description != null && EF.Functions.ILike(m.Description, probe)));
        }

        var total = await query.CountAsync();
        var miniApps = await query
            .OrderBy(m => m.Name)
            .ThenBy(m => m.Slug)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return (miniApps, total);
    }

    public async Task<List<SnMiniApp>> GetMiniAppsByProjectAsync(Guid projectId)
    {
        return await db.MiniApps
            .Include(m => m.Project)
                .ThenInclude(p => p.Developer)
            .Where(m => m.ProjectId == projectId)
            .ToListAsync();
    }

    public async Task<SnMiniApp> CreateMiniAppAsync(
        Guid projectId,
        string slug,
        MiniAppStage stage,
        MiniAppManifest manifest,
        string? iconId = null,
        string? backgroundId = null)
    {
        var project = await db.DevProjects.FindAsync(projectId);
        if (project == null)
            throw new ArgumentException("Project not found");

        // Check if a mini app with this slug already exists globally
        var existingMiniApp = await db.MiniApps
            .FirstOrDefaultAsync(m => m.Slug.ToLower() == slug.ToLowerInvariant());

        if (existingMiniApp != null)
            throw new InvalidOperationException("A mini app with this slug already exists.");

        var existingPlugin = await db.MiniApps
            .FirstOrDefaultAsync(m => m.PluginId == manifest.Id);
        if (existingPlugin != null)
            throw new InvalidOperationException("A plugin with this manifest id already exists.");

        var miniApp = new SnMiniApp
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Stage = stage,
            Manifest = manifest,
            ProjectId = projectId,
            Project = project
        };
        ApplyManifestMetadata(miniApp, manifest);
        await ApplyFileReferencesAsync(miniApp, iconId, backgroundId);

        db.MiniApps.Add(miniApp);
        await db.SaveChangesAsync();

        return miniApp;
    }

    public async Task<SnMiniApp> UpdateMiniAppAsync(
        SnMiniApp miniApp,
        string? slug,
        MiniAppStage? stage,
        MiniAppManifest? manifest,
        string? iconId = null,
        string? backgroundId = null)
    {
        if (slug != null && slug != miniApp.Slug)
        {
            // Check if another mini app with this slug already exists globally
            var existingMiniApp = await db.MiniApps
                .FirstOrDefaultAsync(m => m.Slug.ToLower() == slug.ToLowerInvariant() && m.Id != miniApp.Id);

            if (existingMiniApp != null)
                throw new InvalidOperationException("A mini app with this slug already exists.");

            miniApp.Slug = slug;
        }

        if (stage.HasValue) miniApp.Stage = stage.Value;
        if (manifest != null)
        {
            var existingPlugin = await db.MiniApps
                .FirstOrDefaultAsync(m => m.PluginId == manifest.Id && m.Id != miniApp.Id);
            if (existingPlugin != null)
                throw new InvalidOperationException("A plugin with this manifest id already exists.");

            miniApp.Manifest = manifest;
            ApplyManifestMetadata(miniApp, manifest);
        }

        await ApplyFileReferencesAsync(miniApp, iconId, backgroundId);

        db.Update(miniApp);
        await db.SaveChangesAsync();

        return miniApp;
    }

    private static void ApplyManifestMetadata(SnMiniApp miniApp, MiniAppManifest manifest)
    {
        miniApp.PluginId = manifest.Id;
        miniApp.Name = manifest.Name;
        miniApp.Version = manifest.Version;
        miniApp.Author = manifest.Author;
        miniApp.Description = manifest.Description;
        miniApp.EntryUrl = manifest.EntryUrl ?? string.Empty;
        miniApp.Homepage = manifest.Homepage;
    }

    public async Task SaveAsync(SnMiniApp miniApp, CancellationToken cancellationToken = default)
    {
        db.Update(miniApp);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyFileReferencesAsync(
        SnMiniApp miniApp,
        string? iconId,
        string? backgroundId)
    {
        if (iconId is not null)
            miniApp.Icon = await ResolveFileAsync(iconId, "icon");
        if (backgroundId is not null)
            miniApp.Background = await ResolveFileAsync(backgroundId, "background");
    }

    private async Task<SnCloudFileReferenceObject?> ResolveFileAsync(string fileId, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return null;

        var file = await files.GetFileAsync(new DyGetFileRequest { Id = fileId });
        if (file is null)
            throw new InvalidOperationException($"Invalid {fieldName} file id, unable to find the file on cloud.");

        return SnCloudFileReferenceObject.FromProtoValue(file);
    }

    public async Task<bool> DeleteMiniAppAsync(Guid id)
    {
        var miniApp = await db.MiniApps.FindAsync(id);
        if (miniApp == null)
            return false;

        db.MiniApps.Remove(miniApp);
        await db.SaveChangesAsync();

        return true;
    }
}
