using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Realm;

[ApiController]
[Route("api/realms/public")]
public class RealmPublicController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SnRealm>>> ListCommunityRealms(
        [FromQuery] string? query,
        [FromQuery] int take = 10,
        [FromQuery] int offset = 0
    )
    {
        var realmsQuery = db.Realms
            .Where(r => r.IsCommunity)
            .OrderByDescending(r => r.Members.Count)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
            realmsQuery = realmsQuery.Where(r =>
                EF.Functions.ILike(r.Name, $"%{query}%") ||
                EF.Functions.ILike(r.Description, $"%{query}%")
            );

        var totalCount = await realmsQuery.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var realms = await realmsQuery
            .Take(take)
            .Skip(offset)
            .ToListAsync();
        return Ok(realms);
    }
}
