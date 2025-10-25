using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Develop.Project;

public class DevProjectService(AppDatabase db )
{
    public async Task<SnDevProject> CreateProjectAsync(
        SnDeveloper developer,
        DevProjectController.DevProjectRequest request
    )
    {
        var project = new SnDevProject
        {
            Slug = request.Slug!,
            Name = request.Name!,
            Description = request.Description ?? string.Empty,
            DeveloperId = developer.Id
        };

        db.DevProjects.Add(project);
        await db.SaveChangesAsync();

        return project;
    }

    public async Task<SnDevProject?> GetProjectAsync(Guid id, Guid? developerId = null)
    {
        var query = db.DevProjects.AsQueryable();

        if (developerId.HasValue)
        {
            query = query.Where(p => p.DeveloperId == developerId.Value);
        }

        return await query.FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<SnDevProject>> GetProjectsByDeveloperAsync(Guid developerId)
    {
        return await db.DevProjects
            .Where(p => p.DeveloperId == developerId)
            .ToListAsync();
    }

    public async Task<SnDevProject?> UpdateProjectAsync(
        Guid id,
        Guid developerId,
        DevProjectController.DevProjectRequest request
    )
    {
        var project = await GetProjectAsync(id, developerId);
        if (project == null) return null;

        if (request.Slug != null) project.Slug = request.Slug;
        if (request.Name != null) project.Name = request.Name;
        if (request.Description != null) project.Description = request.Description;

        await db.SaveChangesAsync();
        return project;
    }

    public async Task<bool> DeleteProjectAsync(Guid id, Guid developerId)
    {
        var project = await GetProjectAsync(id, developerId);
        if (project == null) return false;

        db.DevProjects.Remove(project);
        await db.SaveChangesAsync();
        return true;
    }
}