using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Localization;
using DysonNetwork.Sphere.Realm;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

[ApiController]
[Route("/api/chat")]
public class ChatRoomController(
    AppDatabase db,
    ChatRoomService crs,
    RealmService rs,
    IStringLocalizer<NotificationResource> localizer,
    AccountService.AccountServiceClient accounts,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    ActionLogService.ActionLogServiceClient als,
    PusherService.PusherServiceClient pusher,
    AccountClientHelper accountsHelper
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

        if (HttpContext.Items["CurrentUser"] is Account currentUser)
            chatRoom = await crs.LoadDirectMessageMembers(chatRoom, Guid.Parse(currentUser.Id));

        return Ok(chatRoom);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<ChatRoom>>> ListJoinedChatRooms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRooms = await db.ChatMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null)
            .Where(m => m.LeaveAt == null)
            .Include(m => m.ChatRoom)
            .Select(m => m.ChatRoom)
            .ToListAsync();
        chatRooms = await crs.LoadDirectMessageMembers(chatRooms, accountId);
        chatRooms = await crs.SortChatRoomByLastMessage(chatRooms);

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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var relatedUser = await accounts.GetAccountAsync(
            new GetAccountRequest { Id = request.RelatedUserId.ToString() }
        );
        if (relatedUser is null)
            return BadRequest("Related user was not found");

        var hasBlocked = await accounts.HasRelationshipAsync(new GetRelationshipRequest()
        {
            AccountId = currentUser.Id,
            RelatedId = request.RelatedUserId.ToString(),
            Status = -100
        });
        if (hasBlocked?.Value ?? false)
            return StatusCode(403, "You cannot create direct message with a user that blocked you.");

        // Check if DM already exists between these users
        var existingDm = await db.ChatRooms
            .Include(c => c.Members)
            .Where(c => c.Type == ChatRoomType.DirectMessage && c.Members.Count == 2)
            .Where(c => c.Members.Any(m => m.AccountId == Guid.Parse(currentUser.Id)))
            .Where(c => c.Members.Any(m => m.AccountId == request.RelatedUserId))
            .FirstOrDefaultAsync();

        if (existingDm != null)
            return BadRequest("You already have a DM with this user.");

        // Create new DM chat room
        var dmRoom = new ChatRoom
        {
            Type = ChatRoomType.DirectMessage,
            IsPublic = false,
            Members = new List<ChatMember>
            {
                new()
                {
                    AccountId = Guid.Parse(currentUser.Id),
                    Role = ChatMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
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

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.create",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(dmRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        var invitedMember = dmRoom.Members.First(m => m.AccountId == request.RelatedUserId);
        invitedMember.ChatRoom = dmRoom;
        await _SendInviteNotify(invitedMember, currentUser);

        return Ok(dmRoom);
    }

    [HttpGet("direct/{accountId:guid}")]
    [Authorize]
    public async Task<ActionResult<ChatRoom>> GetDirectChatRoom(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var room = await db.ChatRooms
            .Include(c => c.Members)
            .Where(c => c.Type == ChatRoomType.DirectMessage && c.Members.Count == 2)
            .Where(c => c.Members.Any(m => m.AccountId == Guid.Parse(currentUser.Id)))
            .Where(c => c.Members.Any(m => m.AccountId == accountId))
            .FirstOrDefaultAsync();
        if (room is null) return NotFound();

        return Ok(room);
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
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser) return Unauthorized();
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
                    AccountId = Guid.Parse(currentUser.Id),
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        if (request.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(request.RealmId.Value, Guid.Parse(currentUser.Id),
                    RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a moderator to create chat linked to the realm.");
            chatRoom.RealmId = request.RealmId;
        }

        if (request.PictureId is not null)
        {
            try
            {
                var fileResponse = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
                if (fileResponse == null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
                chatRoom.Picture = CloudFileReferenceObject.FromProtoValue(fileResponse);

                await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
                {
                    FileId = fileResponse.Id,
                    Usage = "chatroom.picture",
                    ResourceId = chatRoom.ResourceIdentifier,
                });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("Invalid picture id, unable to find the file on cloud.");
            }
        }

        if (request.BackgroundId is not null)
        {
            try
            {
                var fileResponse = await files.GetFileAsync(new GetFileRequest { Id = request.BackgroundId });
                if (fileResponse == null) return BadRequest("Invalid background id, unable to find the file on cloud.");
                chatRoom.Background = CloudFileReferenceObject.FromProtoValue(fileResponse);

                await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
                {
                    FileId = fileResponse.Id,
                    Usage = "chatroom.background",
                    ResourceId = chatRoom.ResourceIdentifier,
                });
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("Invalid background id, unable to find the file on cloud.");
            }
        }

        db.ChatRooms.Add(chatRoom);
        await db.SaveChangesAsync();

        var chatRoomResourceId = $"chatroom:{chatRoom.Id}";

        if (chatRoom.Picture is not null)
        {
            await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
            {
                FileId = chatRoom.Picture.Id,
                Usage = "chat.room.picture",
                ResourceId = chatRoomResourceId
            });
        }

        if (chatRoom.Background is not null)
        {
            await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
            {
                FileId = chatRoom.Background.Id,
                Usage = "chat.room.background",
                ResourceId = chatRoomResourceId
            });
        }

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.create",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(chatRoom);
    }


    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<ChatRoom>> UpdateChatRoom(Guid id, [FromBody] ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, Guid.Parse(currentUser.Id),
                    RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a realm moderator to update the chat.");
        }
        else if (!await crs.IsMemberWithRole(chatRoom.Id, Guid.Parse(currentUser.Id), ChatMemberRole.Moderator))
            return StatusCode(403, "You need at least be a moderator to update the chat.");

        if (request.RealmId is not null)
        {
            var member = await db.RealmMembers
                .Where(m => m.AccountId == Guid.Parse(currentUser.Id))
                .Where(m => m.RealmId == request.RealmId)
                .FirstOrDefaultAsync();
            if (member is null || member.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a moderator to transfer the chat linked to the realm.");
            chatRoom.RealmId = member.RealmId;
        }

        if (request.PictureId is not null)
        {
            try
            {
                var fileResponse = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
                if (fileResponse == null) return BadRequest("Invalid picture id, unable to find the file on cloud.");

                // Remove old references for pictures
                await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
                {
                    ResourceId = chatRoom.ResourceIdentifier,
                    Usage = "chat.room.picture"
                });

                // Add a new reference
                await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
                {
                    FileId = fileResponse.Id,
                    Usage = "chat.room.picture",
                    ResourceId = chatRoom.ResourceIdentifier
                });

                chatRoom.Picture = CloudFileReferenceObject.FromProtoValue(fileResponse);
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("Invalid picture id, unable to find the file on cloud.");
            }
        }

        if (request.BackgroundId is not null)
        {
            try
            {
                var fileResponse = await files.GetFileAsync(new GetFileRequest { Id = request.BackgroundId });
                if (fileResponse == null) return BadRequest("Invalid background id, unable to find the file on cloud.");

                // Remove old references for backgrounds
                await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
                {
                    ResourceId = chatRoom.ResourceIdentifier,
                    Usage = "chat.room.background"
                });

                // Add a new reference
                await fileRefs.CreateReferenceAsync(new CreateReferenceRequest
                {
                    FileId = fileResponse.Id,
                    Usage = "chat.room.background",
                    ResourceId = chatRoom.ResourceIdentifier
                });

                chatRoom.Background = CloudFileReferenceObject.FromProtoValue(fileResponse);
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("Invalid background id, unable to find the file on cloud.");
            }
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

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.update",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(chatRoom);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteChatRoom(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, Guid.Parse(currentUser.Id),
                    RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a realm moderator to delete the chat.");
        }
        else if (!await crs.IsMemberWithRole(chatRoom.Id, Guid.Parse(currentUser.Id), ChatMemberRole.Owner))
            return StatusCode(403, "You need at least be the owner to delete the chat.");

        var chatRoomResourceId = $"chatroom:{chatRoom.Id}";

        // Delete all file references for this chat room
        await fileRefs.DeleteResourceReferencesAsync(new DeleteResourceReferencesRequest
        {
            ResourceId = chatRoomResourceId
        });

        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            await db.ChatMessages
                .Where(m => m.ChatRoomId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(s => s.DeletedAt, now));
            await db.ChatMembers
                .Where(m => m.ChatRoomId == id)
                .ExecuteUpdateAsync(s => s.SetProperty(s => s.DeletedAt, now));
            await db.SaveChangesAsync();

            db.ChatRooms.Remove(chatRoom);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.delete",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return NoContent();
    }

    [HttpGet("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> GetRoomIdentity(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == Guid.Parse(currentUser.Id) && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();

        if (member == null)
            return NotFound();

        return Ok(await crs.LoadMemberAccount(member));
    }

    [HttpGet("{roomId:guid}/members")]
    public async Task<ActionResult<List<ChatMember>>> ListMembers(Guid roomId,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] bool withStatus = false,
        [FromQuery] string? status = null
    )
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;

        var room = await db.ChatRooms
            .FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();
            var member = await db.ChatMembers
                .FirstOrDefaultAsync(m => m.ChatRoomId == roomId && m.AccountId == Guid.Parse(currentUser.Id));
            if (member is null) return StatusCode(403, "You need to be a member to see members of private chat room.");
        }

        var query = db.ChatMembers
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.LeaveAt == null);

        if (withStatus)
        {
            var members = await query
                .OrderBy(m => m.JoinedAt)
                .ToListAsync();

            var memberStatuses = await accountsHelper.GetAccountStatusBatch(
                members.Select(m => m.AccountId).ToList()
            );

            if (!string.IsNullOrEmpty(status))
            {
                members = members
                    .Select(m =>
                    {
                        m.Status = memberStatuses.TryGetValue(m.AccountId, out var s) ? s : null;
                        return m;
                    })
                    .ToList();
            }

            members = members
                .OrderByDescending(m => m.Status?.IsOnline ?? false)
                .ToList();

            var total = members.Count;
            Response.Headers.Append("X-Total", total.ToString());

            var result = members.Skip(offset).Take(take).ToList();

            return Ok(await crs.LoadMemberAccounts(result));
        }
        else
        {
            var total = await query.CountAsync();
            Response.Headers.Append("X-Total", total.ToString());

            var members = await query
                .OrderBy(m => m.JoinedAt)
                .Skip(offset)
                .Take(take)
                .ToListAsync();
            members = await crs.LoadMemberAccounts(members);

            return Ok(members.Where(m => m.Account is not null).ToList());
        }
    }


    public class ChatMemberRequest
    {
        [Required] public Guid RelatedUserId { get; set; }
        [Required] public int Role { get; set; }
    }

    [HttpPost("invites/{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> InviteMember(Guid roomId,
        [FromBody] ChatMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Get related user account
        var relatedUser =
            await accounts.GetAccountAsync(new GetAccountRequest { Id = request.RelatedUserId.ToString() });
        if (relatedUser == null) return BadRequest("Related user was not found");

        // Check if the user has blocked the current user
        var relationship = await accounts.GetRelationshipAsync(new GetRelationshipRequest
        {
            AccountId = currentUser.Id,
            RelatedId = relatedUser.Id,
            Status = -100
        });

        if (relationship?.Relationship != null && relationship.Relationship.Status == -100)
            return StatusCode(403, "You cannot invite a user that blocked you.");

        var chatRoom = await db.ChatRooms
            .Where(p => p.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Handle realm-owned chat rooms
        if (chatRoom.RealmId is not null)
        {
            var realmMember = await db.RealmMembers
                .Where(m => m.AccountId == accountId)
                .Where(m => m.RealmId == chatRoom.RealmId)
                .FirstOrDefaultAsync();
            if (realmMember is null || realmMember.Role < RealmMemberRole.Moderator)
                return StatusCode(403, "You need at least be a realm moderator to invite members to this chat.");
        }
        else
        {
            var chatMember = await db.ChatMembers
                .Where(m => m.AccountId == accountId)
                .Where(m => m.ChatRoomId == roomId)
                .FirstOrDefaultAsync();
            if (chatMember is null) return StatusCode(403, "You are not even a member of the targeted chat room.");
            if (chatMember.Role < ChatMemberRole.Moderator)
                return StatusCode(403,
                    "You need at least be a moderator to invite other members to this chat room.");
            if (chatMember.Role < request.Role)
                return StatusCode(403, "You cannot invite member with higher permission than yours.");
        }

        var hasExistingMember = await db.ChatMembers
            .Where(m => m.AccountId == request.RelatedUserId)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.LeaveAt == null)
            .AnyAsync();
        if (hasExistingMember)
            return BadRequest("This user has been joined the chat cannot be invited again.");

        var newMember = new ChatMember
        {
            AccountId = Guid.Parse(relatedUser.Id),
            ChatRoomId = roomId,
            Role = request.Role,
        };

        db.ChatMembers.Add(newMember);
        await db.SaveChangesAsync();

        newMember.ChatRoom = chatRoom;
        await _SendInviteNotify(newMember, currentUser);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.invite",
            Meta =
            {
                { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(relatedUser.Id.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(newMember);
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<ChatMember>>> ListChatInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db.ChatMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt == null)
            .Include(e => e.ChatRoom)
            .ToListAsync();

        var chatRooms = members.Select(m => m.ChatRoom).ToList();
        var directMembers =
            (await crs.LoadDirectMessageMembers(chatRooms, accountId)).ToDictionary(c => c.Id, c => c.Members);

        foreach (var member in members.Where(member => member.ChatRoom.Type == ChatRoomType.DirectMessage))
            member.ChatRoom.Members = directMembers[member.ChatRoom.Id];

        return Ok(await crs.LoadMemberAccounts(members));
    }

    [HttpPost("invites/{roomId:guid}/accept")]
    [Authorize]
    public async Task<ActionResult<ChatRoom>> AcceptChatInvite(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();
        _ = crs.PurgeRoomMembersCache(roomId);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = ActionLogType.ChatroomJoin,
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(member);
    }

    [HttpPost("invites/{roomId:guid}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineChatInvite(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return NoContent();
    }

    public class ChatMemberNotifyRequest
    {
        public ChatMemberNotify? NotifyLevel { get; set; }
        public Instant? BreakUntil { get; set; }
    }

    [HttpPatch("{roomId:guid}/members/me/notify")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> UpdateChatMemberNotify(
        Guid roomId,
        [FromBody] ChatMemberNotifyRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var targetMember = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (targetMember is null) return BadRequest("You have not joined this chat room.");
        if (request.NotifyLevel is not null)
            targetMember.Notify = request.NotifyLevel.Value;
        if (request.BreakUntil is not null)
            targetMember.BreakUntil = request.BreakUntil.Value;

        db.ChatMembers.Update(targetMember);
        await db.SaveChangesAsync();

        await crs.PurgeRoomMembersCache(roomId);

        return Ok(targetMember);
    }

    [HttpPatch("{roomId:guid}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<ChatMember>> UpdateChatMemberRole(Guid roomId, Guid memberId, [FromBody] int newRole)
    {
        if (newRole >= ChatMemberRole.Owner) return BadRequest("Unable to set chat member to owner or greater role.");
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Check if the chat room is owned by a realm
        if (chatRoom.RealmId is not null)
        {
            var realmMember = await db.RealmMembers
                .Where(m => m.AccountId == Guid.Parse(currentUser.Id))
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
                    Guid.Parse(currentUser.Id),
                    ChatMemberRole.Moderator,
                    targetMember.Role,
                    newRole
                )
            )
                return StatusCode(403, "You don't have enough permission to edit the roles of members.");

            targetMember.Role = newRole;
            db.ChatMembers.Update(targetMember);
            await db.SaveChangesAsync();

            await crs.PurgeRoomMembersCache(roomId);

            _ = als.CreateActionLogAsync(new CreateActionLogRequest
            {
                Action = "chatrooms.role.edit",
                Meta =
                {
                    { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) },
                    { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(memberId.ToString()) },
                    { "new_role", Google.Protobuf.WellKnownTypes.Value.ForNumber(newRole) }
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
            });

            return Ok(targetMember);
        }

        return BadRequest();
    }

    [HttpDelete("{roomId:guid}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveChatMember(Guid roomId, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Check if the chat room is owned by a realm
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, Guid.Parse(currentUser.Id),
                    RealmMemberRole.Moderator))
                return StatusCode(403, "You need at least be a realm moderator to remove members.");
        }
        else
        {
            if (!await crs.IsMemberWithRole(chatRoom.Id, Guid.Parse(currentUser.Id), ChatMemberRole.Moderator))
                return StatusCode(403, "You need at least be a moderator to remove members.");
        }

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
        _ = crs.PurgeRoomMembersCache(roomId);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.kick",
            Meta =
            {
                { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(memberId.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return NoContent();
    }


    [HttpPost("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult<ChatRoom>> JoinChatRoom(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();
        if (!chatRoom.IsCommunity)
            return StatusCode(403, "This chat room isn't a community. You need an invitation to join.");

        var existingMember = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.AccountId == Guid.Parse(currentUser.Id) && m.ChatRoomId == roomId);
        if (existingMember != null)
            return BadRequest("You are already a member of this chat room.");

        var newMember = new ChatMember
        {
            AccountId = Guid.Parse(currentUser.Id),
            ChatRoomId = roomId,
            Role = ChatMemberRole.Member,
            JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        db.ChatMembers.Add(newMember);
        await db.SaveChangesAsync();
        _ = crs.PurgeRoomMembersCache(roomId);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = ActionLogType.ChatroomJoin,
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return Ok(chatRoom);
    }

    [HttpDelete("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult> LeaveChat(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == Guid.Parse(currentUser.Id))
            .Where(m => m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (member.Role == ChatMemberRole.Owner)
        {
            // Check if this is the only owner
            var otherOwners = await db.ChatMembers
                .Where(m => m.ChatRoomId == roomId)
                .Where(m => m.Role == ChatMemberRole.Owner)
                .Where(m => m.AccountId != Guid.Parse(currentUser.Id))
                .AnyAsync();

            if (!otherOwners)
                return BadRequest("The last owner cannot leave the chat. Transfer ownership first or delete the chat.");
        }

        member.LeaveAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        await db.SaveChangesAsync();
        await crs.PurgeRoomMembersCache(roomId);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = ActionLogType.ChatroomLeave,
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
        });

        return NoContent();
    }

    private async Task _SendInviteNotify(ChatMember member, Account sender)
    {
        string title = localizer["ChatInviteTitle"];

        string body = member.ChatRoom.Type == ChatRoomType.DirectMessage
            ? localizer["ChatInviteDirectBody", sender.Nick]
            : localizer["ChatInviteBody", member.ChatRoom.Name ?? "Unnamed"];

        CultureService.SetCultureInfo(member.Account!.Language);
        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = member.Account.Id.ToString(),
                Notification = new PushNotification
                {
                    Topic = "invites.chats",
                    Title = title,
                    Body = body,
                    IsSavable = true
                }
            }
        );
    }
}