using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Storage;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/chat")]
public class ChatRoomController(AppDatabase db, FileService fs) : ControllerBase
{
    [HttpGet("{id:long}")]
    public async Task<ActionResult<ChatRoom>> GetChatRoom(long id)
    {
        var chatRoom = await db.ChatRooms
            .Where(c => c.Id == id)
            .Include(e => e.Picture)
            .Include(e => e.Background)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();
        return Ok(chatRoom);
    }

    [HttpGet]
    public async Task<ActionResult<List<ChatRoom>>> ListJoinedChatRooms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt != null)
            .Include(e => e.ChatRoom)
            .Include(e => e.ChatRoom.Picture)
            .Include(e => e.ChatRoom.Background)
            .Include(e => e.ChatRoom.Type == ChatRoomType.DirectMessage ? e.ChatRoom.Members : null)
            .Select(m => m.ChatRoom)
            .ToListAsync();

        return members.ToList();
    }

    public class ChatRoomRequest
    {
        [Required] [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
        public long? RealmId { get; set; }
    }

    [HttpPost]
    public async Task<ActionResult<ChatRoom>> CreateChatRoom(ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        if (request.Name is null) return BadRequest("You cannot create a chat room without a name.");

        var chatRoom = new ChatRoom
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Members = new List<ChatMember>
            {
                new()
                {
                    Role = ChatMemberRole.Owner,
                    AccountId = currentUser.Id
                }
            }
        };

        if (request.RealmId is not null)
        {
            var member = await db.RealmMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.RealmId == request.RealmId)
                .FirstOrDefaultAsync();
            if (member is null || member.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a moderator to create chat linked to the realm.");
            chatRoom.RealmId = member.RealmId;
        }

        if (request.PictureId is not null)
        {
            chatRoom.Picture = await db.Files.FindAsync(request.PictureId);
            if (chatRoom.Picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
        }

        if (request.BackgroundId is not null)
        {
            chatRoom.Background = await db.Files.FindAsync(request.BackgroundId);
            if (chatRoom.Background is null)
                return BadRequest("Invalid background id, unable to find the file on cloud.");
        }

        db.ChatRooms.Add(chatRoom);
        await db.SaveChangesAsync();

        if (chatRoom.Picture is not null)
            await fs.MarkUsageAsync(chatRoom.Picture, 1);
        if (chatRoom.Background is not null)
            await fs.MarkUsageAsync(chatRoom.Background, 1);

        return Ok(chatRoom);
    }

    [HttpPut("{id:long}")]
    public async Task<ActionResult<ChatRoom>> UpdateChatRoom(long id, [FromBody] ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .Include(c => c.Picture)
            .Include(c => c.Background)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        if (chatRoom.RealmId is not null)
        {
            var realmMember = await db.RealmMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.RealmId == chatRoom.RealmId)
                .FirstOrDefaultAsync();
            if (realmMember is null || realmMember.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a realm moderator to update the chat."); 
        }
        else
        {
            var chatMember = await db.ChatMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.ChatRoomId == chatRoom.Id)
                .FirstOrDefaultAsync();
            if (chatMember is null || chatMember.Role < ChatMemberRole.Moderator)
                return StatusCode(403, "You need at least be a moderator to update the chat.");
        }

        if (request.RealmId is not null)
        {
            var member = await db.RealmMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.RealmId == request.RealmId)
                .FirstOrDefaultAsync();
            if (member is null || member.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a moderator to transfer the chat linked to the realm.");
            chatRoom.RealmId = member.RealmId;
        }

        if (request.PictureId is not null)
        {
            var picture = await db.Files.FindAsync(request.PictureId);
            if (picture is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
            await fs.MarkUsageAsync(picture, 1);
            if (chatRoom.Picture is not null) await fs.MarkUsageAsync(chatRoom.Picture, -1);
            chatRoom.Picture = picture;
        }

        if (request.BackgroundId is not null)
        {
            var background = await db.Files.FindAsync(request.BackgroundId);
            if (background is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
            await fs.MarkUsageAsync(background, 1);
            if (chatRoom.Background is not null) await fs.MarkUsageAsync(chatRoom.Background, -1);
            chatRoom.Background = background;
        }

        if (request.Name is not null)
            chatRoom.Name = request.Name;
        if (request.Description is not null)
            chatRoom.Description = request.Description;

        db.ChatRooms.Update(chatRoom);
        await db.SaveChangesAsync();

        return Ok(chatRoom);
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteChatRoom(long id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .Include(c => c.Picture)
            .Include(c => c.Background)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();
        
        if (chatRoom.RealmId is not null)
        {
            var realmMember = await db.RealmMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.RealmId == chatRoom.RealmId)
                .FirstOrDefaultAsync();
            if (realmMember is null || realmMember.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a realm moderator to delete the chat."); 
        }
        else
        {
            var chatMember = await db.ChatMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.ChatRoomId == chatRoom.Id)
                .FirstOrDefaultAsync();
            if (chatMember is null || chatMember.Role < ChatMemberRole.Owner)
                return StatusCode(403, "You need at least be the owner to delete the chat.");
        }

        db.ChatRooms.Remove(chatRoom);
        await db.SaveChangesAsync();

        if (chatRoom.Picture is not null)
            await fs.MarkUsageAsync(chatRoom.Picture, -1);
        if (chatRoom.Background is not null)
            await fs.MarkUsageAsync(chatRoom.Background, -1);

        return NoContent();
    }
}