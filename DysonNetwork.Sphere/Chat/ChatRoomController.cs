using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Permission;
using DysonNetwork.Sphere.Realm;
using DysonNetwork.Sphere.Storage;
using Microsoft.AspNetCore.Authorization;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/chat")]
public class ChatRoomController(
    AppDatabase db,
    FileService fs,
    ChatRoomService crs,
    RealmService rs,
    ActionLogService als,
    NotificationService nty
) : ControllerBase
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

        if (HttpContext.Items["CurrentUser"] is Account.Account currentUser)
            chatRoom = await crs.LoadDirectMessageMembers(chatRoom, currentUser.Id);

        return Ok(chatRoom);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<ChatRoom>>> ListJoinedChatRooms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser)
            return Unauthorized();
        var userId = currentUser.Id;

        var chatRooms = await db.ChatMembers
            .Where(m => m.AccountId == userId)
            .Where(m => m.JoinedAt != null)
            .Where(m => m.LeaveAt == null)
            .Include(m => m.ChatRoom)
            .Select(m => m.ChatRoom)
            .ToListAsync();
        chatRooms = await crs.LoadDirectMessageMembers(chatRooms, userId);

        return Ok(chatRooms);
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
            return BadRequest("You already have a DM with this user.");

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

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomCreate,
            new Dictionary<string, object> { { "chatroom_id", dmRoom.Id } }, Request
        );

        var invitedMember = dmRoom.Members.First(m => m.AccountId == request.RelatedUserId);
        await _SendInviteNotify(invitedMember);

        return Ok(dmRoom);
    }


    public class ChatRoomRequest
    {
        [Required] [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        [MaxLength(32)] public string? PictureId { get; set; }
        [MaxLength(32)] public string? BackgroundId { get; set; }
        public Guid? RealmId { get; set; }
        public bool? IsCommunity { get; set; }
        public bool? IsPublic { get; set; }
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
            IsCommunity = request.IsCommunity ?? false,
            IsPublic = request.IsPublic ?? false,
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

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomCreate,
            new Dictionary<string, object> { { "chatroom_id", chatRoom.Id } }, Request
        );

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
        if (request.IsCommunity is not null)
            chatRoom.IsCommunity = request.IsCommunity.Value;
        if (request.IsPublic is not null)
            chatRoom.IsPublic = request.IsPublic.Value;

        db.ChatRooms.Update(chatRoom);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomUpdate,
            new Dictionary<string, object> { { "chatroom_id", chatRoom.Id } }, Request
        );

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

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomDelete,
            new Dictionary<string, object> { { "chatroom_id", chatRoom.Id } }, Request
        );

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
            .Where(m => m.LeaveAt == null) // Add this condition to exclude left members
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

        // Check if a user has previously left the chat
        var hasExistingMember = await db.ChatMembers
            .Where(m => m.AccountId == request.RelatedUserId)
            .Where(m => m.ChatRoomId == roomId)
            .AnyAsync();

        if (hasExistingMember)
            return BadRequest("This user has been joined the chat or leave cannot be invited again.");

        var newMember = new ChatMember
        {
            AccountId = relatedUser.Id,
            ChatRoomId = roomId,
            Role = request.Role,
        };

        db.ChatMembers.Add(newMember);
        await db.SaveChangesAsync();

        await _SendInviteNotify(newMember);

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomInvite,
            new Dictionary<string, object> { { "chatroom_id", chatRoom.Id }, { "account_id", relatedUser.Id } }, Request
        );

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

        var chatRooms = members.Select(m => m.ChatRoom).ToList();
        var directMembers =
            (await crs.LoadDirectMessageMembers(chatRooms, userId)).ToDictionary(c => c.Id, c => c.Members);

        foreach (var member in members.Where(member => member.ChatRoom.Type == ChatRoomType.DirectMessage))
            member.ChatRoom.Members = directMembers[member.ChatRoom.Id];

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
        crs.PurgeRoomMembersCache(roomId);

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomJoin,
            new Dictionary<string, object> { { "chatroom_id", roomId } }, Request
        );

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

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPatch("{roomId:guid}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> UpdateChatMemberRole(Guid roomId, Guid memberId,
        [FromBody] ChatMemberRole newRole)
    {
        if (newRole >= ChatMemberRole.Owner) return BadRequest("Unable to set chat member to owner or greater role.");
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
            var targetMember = await db.ChatMembers
                .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (targetMember is null) return NotFound();

            // Check if the current user has permission to change roles
            if (
                !await crs.IsMemberWithRole(
                    chatRoom.Id,
                    currentUser.Id,
                    ChatMemberRole.Moderator,
                    targetMember.Role,
                    newRole
                )
            )
                return StatusCode(403, "You don't have enough permission to edit the roles of members.");

            targetMember.Role = newRole;
            db.ChatMembers.Update(targetMember);
            await db.SaveChangesAsync();

            als.CreateActionLogFromRequest(
                ActionLogType.RealmAdjustRole,
                new Dictionary<string, object>
                    { { "chatroom_id", roomId }, { "account_id", memberId }, { "new_role", newRole } },
                Request
            );

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
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, currentUser.Id, RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a realm moderator to remove members.");
        }
        else
        {
            if (!await crs.IsMemberWithRole(chatRoom.Id, currentUser.Id, ChatMemberRole.Moderator))
                return StatusCode(403, "You need at least be a moderator to remove members.");

            // Find the target member
            var member = await db.ChatMembers
                .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (member is null) return NotFound();

            // Check if the current user has sufficient permissions
            if (!await crs.IsMemberWithRole(chatRoom.Id, memberId, member.Role))
                return StatusCode(403, "You cannot remove members with equal or higher roles.");

            member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();
            crs.PurgeRoomMembersCache(roomId);

            als.CreateActionLogFromRequest(
                ActionLogType.ChatroomKick,
                new Dictionary<string, object> { { "chatroom_id", roomId }, { "account_id", memberId } }, Request
            );

            return NoContent();
        }

        return BadRequest();
    }


    [HttpPost("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult<ChatRoom>> JoinChatRoom(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();
        if (!chatRoom.IsCommunity)
            return StatusCode(403, "This chat room isn't a community. You need an invitation to join.");

        var existingMember = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.AccountId == currentUser.Id && m.ChatRoomId == roomId);
        if (existingMember != null)
            return BadRequest("You are already a member of this chat room.");

        var newMember = new ChatMember
        {
            AccountId = currentUser.Id,
            ChatRoomId = roomId,
            Role = ChatMemberRole.Member,
            JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        db.ChatMembers.Add(newMember);
        await db.SaveChangesAsync();
        crs.PurgeRoomMembersCache(roomId);

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomJoin,
            new Dictionary<string, object> { { "chatroom_id", roomId } }, Request
        );

        return Ok(chatRoom);
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

        member.LeaveAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        crs.PurgeRoomMembersCache(roomId);

        als.CreateActionLogFromRequest(
            ActionLogType.ChatroomLeave,
            new Dictionary<string, object> { { "chatroom_id", roomId } }, Request
        );

        return NoContent();
    }

    private async Task _SendInviteNotify(ChatMember member)
    {
        await nty.SendNotification(member.Account, "invites.chats", "New Chat Invitation", null,
            $"You just got invited to join {member.ChatRoom.Name}");
    }
}