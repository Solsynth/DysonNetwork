using DysonNetwork.Sphere.Chat;
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
    public async Task<ActionResult<List<ChatRoom>>> ListRealmChat(string slug)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account.Account;

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();
        if (!realm.IsPublic)
        {
            if (currentUser is null) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Normal))
                return StatusCode(403, "You need at least one member to view the realm's chat.");
        }

        var chatRooms = await db.ChatRooms
            .Where(c => c.RealmId == realm.Id)
            .ToListAsync();

        return Ok(chatRooms);
    }
}
