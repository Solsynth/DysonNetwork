using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/api/realms/{slug}")]
public class RealmChatController(AppDatabase db, RemoteRealmService rs) : ControllerBase
{
    [HttpGet("chat")]
    [Authorize]
    public async Task<ActionResult<List<SnChatRoom>>> ListRealmChat(string slug)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Shared.Proto.Account;
        var accountId = currentUser is null ? Guid.Empty : Guid.Parse(currentUser.Id);

        var realm = await rs.GetRealmBySlug(slug);
        if (!realm.IsPublic)
        {
            if (currentUser is null) return Unauthorized();
                if (!await rs.IsMemberWithRole(realm.Id, accountId, [RealmMemberRole.Normal]))
                return StatusCode(403, "You need at least one member to view the realm's chat.");
        }

        var chatRooms = await db.ChatRooms
            .Where(c => c.RealmId == realm.Id)
            .ToListAsync();

        return Ok(chatRooms);
    }
}
