using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Drive.Storage;

[ApiController]
[Route("/api/pools")]
public class FilePoolController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<FilePool>>> ListUsablePools()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var pools = await db.Pools
            .Where(p => p.PolicyConfig.PublicUsable || p.AccountId == accountId)
            .ToListAsync();

        return Ok(pools);
    }
}