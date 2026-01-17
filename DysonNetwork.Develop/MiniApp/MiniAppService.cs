using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.MiniApp;

public class MiniAppService(AppDatabase db)
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
            .FirstOrDefaultAsync(m => m.Slug == slug);
    }

    public async Task<List<SnMiniApp>> GetMiniAppsByProjectAsync(Guid projectId)
    {
        return await db.MiniApps
            .Where(m => m.ProjectId == projectId)
            .ToListAsync();
    }

    public async Task<SnMiniApp> CreateMiniAppAsync(Guid projectId, string slug, MiniAppStage stage, MiniAppManifest manifest)
    {
        var project = await db.DevProjects.FindAsync(projectId);
        if (project == null)
            throw new ArgumentException("Project not found");

        // Check if a mini app with this slug already exists globally
        var existingMiniApp = await db.MiniApps
            .FirstOrDefaultAsync(m => m.Slug == slug);

        if (existingMiniApp != null)
            throw new InvalidOperationException("A mini app with this slug already exists.");

        var miniApp = new SnMiniApp
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Stage = stage,
            Manifest = manifest,
            ProjectId = projectId,
            Project = project
        };

        db.MiniApps.Add(miniApp);
        await db.SaveChangesAsync();

        return miniApp;
    }

    public async Task<SnMiniApp> UpdateMiniAppAsync(SnMiniApp miniApp, string? slug, MiniAppStage? stage, MiniAppManifest? manifest)
    {
        if (slug != null && slug != miniApp.Slug)
        {
            // Check if another mini app with this slug already exists globally
            var existingMiniApp = await db.MiniApps
                .FirstOrDefaultAsync(m => m.Slug == slug && m.Id != miniApp.Id);

            if (existingMiniApp != null)
                throw new InvalidOperationException("A mini app with this slug already exists.");

            miniApp.Slug = slug;
        }

        if (stage.HasValue) miniApp.Stage = stage.Value;
        if (manifest != null) miniApp.Manifest = manifest;

        db.Update(miniApp);
        await db.SaveChangesAsync();

        return miniApp;
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
