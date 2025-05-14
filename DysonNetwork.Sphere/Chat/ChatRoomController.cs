using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/chat")]
public class ChatRoomController(AppDatabase db, FileService fs, ChatRoomService crs, RealmService rs) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ChatRoom>> GetChatRoom(Guid id)
    {
        var chatRoom = await db.ChatRooms
            .Where(c => c.Id == id)
            .Include(e => e.Realm)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();
        if (chatRoom.Type != ChatRoomType.DirectMessage) return Ok(chatRoom);

        // Preload members for direct messages
        var currentUser = HttpContext.Items["CurrentUser"] as Account.Account;
        var directMembers = await db.ChatMembers
            .Where(m => (currentUser != null && m.AccountId != currentUser!.Id))
            .Include(m => m.Account)
            .Include(m => m.Account.Profile)
            .ToListAsync();
        chatRoom.DirectMembers = directMembers.Select(ChatMemberTransmissionObject.FromEntity).ToList();
        return Ok(chatRoom);
    }

    [HttpGet]
    public async Task<ActionResult<List<ChatRoom>>> ListJoinedChatRooms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var userId = currentUser.Id;

        var chatRooms = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt != null)
            .Include(m => m.ChatRoom)
            .Select(m => m.ChatRoom)
            .ToListAsync();

        var directRoomsId = chatRooms
            .Where(r => r.Type == ChatRoomType.DirectMessage)
            .Select(r => r.Id)
            .ToList();
        var directMembers = directRoomsId.Count != 0
            ? await db.ChatMembers
                .Where(m => directRoomsId.Contains(m.ChatRoomId))
                .Where(m => m.AccountId != userId)
                .Include(m => m.Account)
                .Include(m => m.Account.Profile)
                .ToDictionaryAsync(m => m.ChatRoomId, m => m)
            : new Dictionary<Guid, ChatMember>();

        // Map the results
        var result = chatRooms.Select(r =>
        {
            if (r.Type == ChatRoomType.DirectMessage && directMembers.TryGetValue(r.Id, out var otherMember))
                r.DirectMembers = new List<ChatMemberTransmissionObject>
                    { ChatMemberTransmissionObject.FromEntity(otherMember) };
            return r;
        }).ToList();

        return Ok(result);
    }

    public class DirectMessageRequest
    {
        [Required] public Guid RelatedUserId { get; set; }
    }

    [HttpPost("direct")]
    [Authorize]
    public async Task<ActionResult<ChatRoom>> CreateDirectMessage([FromBody] DirectMessageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var relatedUser = await db.Accounts.FindAsync(request.RelatedUserId);
        if (relatedUser is null)
            return BadRequest("Related user was not found");

        // Check if DM already exists between these users
        var existingDm = await db.ChatRooms
            .Include(c => c.Members)
            .Where(c => c.Type == ChatRoomType.DirectMessage && c.Members.Count == 2)
            .Where(c => c.Members.Any(m => m.AccountId == currentUser.Id))
            .Where(c => c.Members.Any(m => m.AccountId == request.RelatedUserId))
            .FirstOrDefaultAsync();

        if (existingDm != null)
            return Ok(existingDm); // Return existing DM if found

        // Create new DM chat room
        var dmRoom = new ChatRoom
        {
            Name = $"DM between #{currentUser.Id} and #{request.RelatedUserId}",
            Type = ChatRoomType.DirectMessage,
            IsPublic = false,
            Members = new List<ChatMember>
            {
                new()
                {
                    AccountId = currentUser.Id,
                    Role = ChatMemberRole.Owner,
                    JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
                },
                new()
                {
                    AccountId = request.RelatedUserId,
                    Role = ChatMemberRole.Member,
                    JoinedAt = null, // Pending status
                }
            }
        };

        db.ChatRooms.Add(dmRoom);
        await db.SaveChangesAsync();

        var invitedMember = dmRoom.Members.First(m => m.AccountId == request.RelatedUserId);
        await crs.SendInviteNotify(invitedMember);

        return Ok(dmRoom);
    }


    public class ChatRoomRequest
    {
        [Required] [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
        public Guid? RealmId { get; set; }
    }

    [HttpPost]
    [Authorize]
    [RequiredPermission("global", "chat.create")]
    public async Task<ActionResult<ChatRoom>> CreateChatRoom(ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        if (request.Name is null) return BadRequest("You cannot create a chat room without a name.");

        var chatRoom = new ChatRoom
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Type = ChatRoomType.Group,
            Members = new List<ChatMember>
            {
                new()
                {
                    Role = ChatMemberRole.Owner,
                    AccountId = currentUser.Id,
                    JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        if (request.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(request.RealmId.Value, currentUser.Id, RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a moderator to create chat linked to the realm.");
            chatRoom.RealmId = request.RealmId;
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


    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ChatRoom>> UpdateChatRoom(Guid id, [FromBody] ChatRoomRequest request)
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
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, currentUser.Id, RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a realm moderator to update the chat.");
        }
        else if (!await crs.IsMemberWithRole(chatRoom.Id, currentUser.Id, ChatMemberRole.Moderator))
            return StatusCode(403, "You need at least be a moderator to update the chat.");

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

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteChatRoom(Guid id)
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
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, currentUser.Id, RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a realm moderator to delete the chat.");
        }
        else if (!await crs.IsMemberWithRole(chatRoom.Id, currentUser.Id, ChatMemberRole.Owner))
            return StatusCode(403, "You need at least be the owner to delete the chat.");

        db.ChatRooms.Remove(chatRoom);
        await db.SaveChangesAsync();

        if (chatRoom.Picture is not null)
            await fs.MarkUsageAsync(chatRoom.Picture, -1);
        if (chatRoom.Background is not null)
            await fs.MarkUsageAsync(chatRoom.Background, -1);

        return NoContent();
    }

    [HttpGet("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> GetRoomIdentity(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
            .Include(m => m.Account)
            .Include(m => m.Account.Profile)
            .FirstOrDefaultAsync();

        if (member == null)
            return NotFound();

        return Ok(member);
    }

    [HttpGet("{roomId:guid}/members")]
    public async Task<ActionResult<List<ChatMember>>> ListMembers(Guid roomId, [FromQuery] int take = 20,
        [FromQuery] int skip = 0)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account.Account;

        var room = await db.ChatRooms
            .FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();
            var member = await db.ChatMembers
                .FirstOrDefaultAsync(m => m.ChatRoomId == roomId && m.AccountId == currentUser.Id);
            if (member is null) return StatusCode(403, "You need to be a member to see members of private chat room.");
        }

        var query = db.ChatMembers
            .Where(m => m.ChatRoomId == roomId)
            .Include(m => m.Account)
            .Include(m => m.Account.Profile);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var members = await query
            .OrderBy(m => m.JoinedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

        return Ok(members);
    }

    public class ChatMemberRequest
    {
        [Required] public Guid RelatedUserId { get; set; }
        [Required] public ChatMemberRole Role { get; set; }
    }

    [HttpPost("invites/{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> InviteMember(Guid roomId,
        [FromBody] ChatMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var relatedUser = await db.Accounts.FindAsync(request.RelatedUserId);
        if (relatedUser is null) return BadRequest("Related user was not found");

        var chatRoom = await db.ChatRooms
            .Where(p => p.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Handle realm-owned chat rooms
        if (chatRoom.RealmId is not null)
        {
            var realmMember = await db.RealmMembers
                .Where(m => m.AccountId == userId)
                .Where(m => m.RealmId == chatRoom.RealmId)
                .FirstOrDefaultAsync();
            if (realmMember is null || realmMember.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a realm moderator to invite members to this chat.");
        }
        else
        {
            var chatMember = await db.ChatMembers
                .Where(m => m.AccountId == userId)
                .Where(m => m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (chatMember is null) return StatusCode(403, "You are not even a member of the targeted chat room.");
            if (chatMember.Role < ChatMemberRole.Moderator)
                return StatusCode(403,
                    "You need at least be a moderator to invite other members to this chat room.");
            if (chatMember.Role < request.Role)
                return StatusCode(403, "You cannot invite member with higher permission than yours.");
        }

        var newMember = new ChatMember
        {
            AccountId = relatedUser.Id,
            ChatRoomId = roomId,
            Role = request.Role,
        };

        db.ChatMembers.Add(newMember);
        await db.SaveChangesAsync();

        await crs.SendInviteNotify(newMember);

        return Ok(newMember);
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<ChatMember>>> ListChatInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var members = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt == null)
            .Include(e => e.ChatRoom)
            .Include(e => e.Account)
            .Include(e => e.Account.Profile)
            .ToListAsync();

        return members.ToList();
    }

    [HttpPost("invites/{roomId:guid}/accept")]
    [Authorize]
    public async Task<ActionResult<ChatRoom>> AcceptChatInvite(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        return Ok(member);
    }

    [HttpPost("invites/{roomId:guid}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineChatInvite(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();
        var userId = currentUser.Id;

        var member = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        db.ChatMembers.Remove(member);
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPatch("{roomId:guid}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> UpdateChatMemberRole(Guid roomId, Guid memberId,
        [FromBody] ChatMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Check if the chat room is owned by a realm
        if (chatRoom.RealmId is not null)
        {
            var realmMember = await db.RealmMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.RealmId == chatRoom.RealmId)
                .FirstOrDefaultAsync();
            if (realmMember is null || realmMember.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a realm moderator to change member roles.");
        }
        else
        {
            // Check if the current user has permission to change roles
            var currentMember = await db.ChatMembers
                .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (currentMember is null || currentMember.Role < ChatMemberRole.Moderator)
                return StatusCode(403, "You need at least be a moderator to change member roles.");

            // Find the target member
            var targetMember = await db.ChatMembers
                .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (targetMember is null) return NotFound();

            // Check if current user has sufficient permissions
            if (currentMember.Role <= targetMember.Role)
                return StatusCode(403, "You cannot modify the role of members with equal or higher roles.");
            if (currentMember.Role <= request.Role)
                return StatusCode(403, "You cannot assign a role equal to or higher than your own.");

            targetMember.Role = request.Role;
            db.ChatMembers.Update(targetMember);
            await db.SaveChangesAsync();

            return Ok(targetMember);
        }

        return BadRequest();
    }

    [HttpDelete("{roomId:guid}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveChatMember(Guid roomId, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Check if the chat room is owned by a realm
        if (chatRoom.RealmId is not null)
        {
            var realmMember = await db.RealmMembers
                .Where(m => m.AccountId == currentUser.Id)
                .Where(m => m.RealmId == chatRoom.RealmId)
                .FirstOrDefaultAsync();
            if (realmMember is null || realmMember.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a realm moderator to remove members.");
        }
        else
        {
            // Check if the current user has permission to remove members
            var currentMember = await db.ChatMembers
                .Where(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (currentMember is null || currentMember.Role < ChatMemberRole.Moderator)
                return StatusCode(403, "You need at least be a moderator to remove members.");

            // Find the target member
            var targetMember = await db.ChatMembers
                .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (targetMember is null) return NotFound();

            // Check if current user has sufficient permissions
            if (currentMember.Role <= targetMember.Role)
                return StatusCode(403, "You cannot remove members with equal or higher roles.");

            db.ChatMembers.Remove(targetMember);
            await db.SaveChangesAsync();

            return NoContent();
        }

        return BadRequest();
    }

    [HttpDelete("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult> LeaveChat(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == currentUser.Id)
            .Where(m => m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (member.Role == ChatMemberRole.Owner)
        {
            // Check if this is the only owner
            var otherOwners = await db.ChatMembers
                .Where(m => m.ChatRoomId == roomId)
                .Where(m => m.Role == ChatMemberRole.Owner)
                .Where(m => m.AccountId != currentUser.Id)
                .AnyAsync();

            if (!otherOwners)
                return BadRequest("The last owner cannot leave the chat. Transfer ownership first or delete the chat.");
        }

        db.ChatMembers.Remove(member);
        await db.SaveChangesAsync();

        return NoContent();
    }
}