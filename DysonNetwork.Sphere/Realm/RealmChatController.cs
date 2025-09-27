using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Realm;

[ApiController]
[Route("/api/realms/{slug}")]
public class RealmChatController(AppDatabase db, RealmService rs) : ControllerBase
{
    [HttpGet("chat")]
    [Authorize]
    public async Task<ActionResult<List<SnChatRoom>>> ListRealmChat(string slug)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();
        if (!realm.IsPublic)
        {
            if (currentUser is null) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, accountId, RealmMemberRole.Normal))
                return StatusCode(403, "You need at least one member to view the realm's chat.");
        }

        var chatRooms = await db.ChatRooms
            .Where(c => c.RealmId == realm.Id)
            .ToListAsync();

        return Ok(chatRooms);
    }
}
