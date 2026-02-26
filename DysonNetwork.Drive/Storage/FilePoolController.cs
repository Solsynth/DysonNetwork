using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Drive.Storage;

[ApiController]
[Route("/api/pools")]
public class FilePoolController(AppDatabase db, FileService fs) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<FilePool>>> ListUsablePools()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var pools = await db.Pools
            .Where(p => p.PolicyConfig.PublicUsable || p.AccountId == accountId)
            .Where(p => !p.IsHidden || p.AccountId == accountId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync();
        pools = pools.Select(p =>
        {
            p.StorageConfig.SecretId = string.Empty;
            p.StorageConfig.SecretKey = string.Empty;
            return p;
        }).ToList();

        return Ok(pools);
    }

    [Authorize]
    [HttpDelete("{id:guid}/recycle")]
    public async Task<ActionResult> DeleteFilePoolRecycledFiles(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var pool = await fs.GetPoolAsync(id);
        if (pool is null) return NotFound();
        if (!currentUser.IsSuperuser && pool.AccountId != accountId) return Unauthorized();

        var count = await fs.DeletePoolRecycledFilesAsync(id);
        return Ok(new { Count = count });
    }
}
