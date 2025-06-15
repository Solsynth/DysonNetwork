using DysonNetwork.Sphere.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Auth.OpenId;

[ApiController]
[Route("/api/connections")]
[Authorize]
public class ConnectionController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AccountConnection>>> GetConnections()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var connections = await db.AccountConnections
            .Where(c => c.AccountId == currentUser.Id)
            .Select(c => new { c.Id, c.AccountId, c.Provider, c.ProvidedIdentifier })
            .ToListAsync();
        return Ok(connections);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> RemoveConnection(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var connection = await db.AccountConnections
            .Where(c => c.Id == id && c.AccountId == currentUser.Id)
            .FirstOrDefaultAsync();
        if (connection == null)
            return NotFound();

        db.AccountConnections.Remove(connection);
        await db.SaveChangesAsync();

        return Ok();
    }
}