using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using NodaTime;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Messager.Chat;

[ApiController]
[Route("/api/chat")]
public class ChatRoomController(
    AppDatabase db,
    ChatRoomService crs,
    RemoteRealmService rs,
    AccountService.AccountServiceClient accounts,
    FileService.FileServiceClient files,
    ActionLogService.ActionLogServiceClient als,
    RingService.RingServiceClient pusher,
    RemoteAccountService remoteAccountsHelper
) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SnChatRoom>> GetChatRoom(Guid id)
    {
        var chatRoom = await db.ChatRooms
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        if (chatRoom.RealmId != null)
            chatRoom.Realm = await rs.GetRealm(chatRoom.RealmId.Value.ToString());

        if (chatRoom.Type != ChatRoomType.DirectMessage) return Ok(chatRoom);

        chatRoom = await crs.LoadChatRealms(chatRoom);

        if (HttpContext.Items["CurrentUser"] is Account currentUser)
            chatRoom = await crs.LoadDirectMessageMembers(chatRoom, Guid.Parse(currentUser.Id));

        return Ok(chatRoom);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnChatRoom>>> ListJoinedChatRooms()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRooms = await db.ChatMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Include(m => m.ChatRoom)
            .Select(m => m.ChatRoom)
            .ToListAsync();
        chatRooms = await crs.LoadChatRealms(chatRooms);
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
    public async Task<ActionResult<SnChatRoom>> CreateDirectMessage([FromBody] DirectMessageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

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
        var dmRoom = new SnChatRoom
        {
            Type = ChatRoomType.DirectMessage,
            IsPublic = false,
            AccountId = accountId,
            Members = new List<SnChatMember>
            {
                new()
                {
                    AccountId = accountId,
                    JoinedAt = SystemClock.Instance.GetCurrentInstant()
                },
                new()
                {
                    AccountId = request.RelatedUserId,
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
        await SendInviteNotify(invitedMember, currentUser);

        return Ok(dmRoom);
    }

    [HttpGet("direct/{accountId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnChatRoom>> GetDirectChatRoom(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var currentId = Guid.Parse(currentUser.Id);

        var room = await db.ChatRooms
            .Include(c => c.Members)
            .Where(c => c.Type == ChatRoomType.DirectMessage && c.Members.Count == 2)
            .Where(c => c.Members.Any(m => m.AccountId == currentId))
            .Where(c => c.Members.Any(m => m.AccountId == accountId))
            .FirstOrDefaultAsync();
        if (room is null) return NotFound();

        return Ok(room);
    }

    public class ChatRoomRequest
    {
        [Required][MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        [MaxLength(32)] public string? PictureId { get; set; }
        [MaxLength(32)] public string? BackgroundId { get; set; }
        public Guid? RealmId { get; set; }
        public bool? IsCommunity { get; set; }
        public bool? IsPublic { get; set; }
    }

    [HttpPost]
    [Authorize]
    [AskPermission("chat.create")]
    public async Task<ActionResult<SnChatRoom>> CreateChatRoom(ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        if (request.Name is null) return BadRequest("You cannot create a chat room without a name.");
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = new SnChatRoom
        {
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            IsCommunity = request.IsCommunity ?? false,
            IsPublic = request.IsPublic ?? false,
            Type = ChatRoomType.Group,
            AccountId = accountId,
            Members = new List<SnChatMember>
            {
                new()
                {
                    AccountId = accountId,
                    JoinedAt = SystemClock.Instance.GetCurrentInstant()
                }
            }
        };

        if (request.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(request.RealmId.Value, Guid.Parse(currentUser.Id),
                    [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a moderator to create chat linked to the realm.");
            chatRoom.RealmId = request.RealmId;
        }

        if (request.PictureId is not null)
        {
            try
            {
                var fileResponse = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
                if (fileResponse == null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
                chatRoom.Picture = SnCloudFileReferenceObject.FromProtoValue(fileResponse);
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
                chatRoom.Background = SnCloudFileReferenceObject.FromProtoValue(fileResponse);
            }
            catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return BadRequest("Invalid background id, unable to find the file on cloud.");
            }
        }

        db.ChatRooms.Add(chatRoom);
        await db.SaveChangesAsync();

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
    public async Task<ActionResult<SnChatRoom>> UpdateChatRoom(Guid id, [FromBody] ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to update the chat.");
        }
        else if (chatRoom.Type == ChatRoomType.DirectMessage && !await crs.IsChatMember(chatRoom.Id, accountId))
            return StatusCode(403, "You need be part of the DM to update the chat.");
        else if (chatRoom.AccountId != accountId)
            return StatusCode(403, "You need be the owner to update the chat.");

        if (request.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(request.RealmId.Value, Guid.Parse(currentUser.Id),
                    [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a moderator to transfer the chat linked to the realm.");
            chatRoom.RealmId = request.RealmId;
        }

        if (request.PictureId is not null)
        {
            try
            {
                var fileResponse = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
                if (fileResponse == null) return BadRequest("Invalid picture id, unable to find the file on cloud.");

                chatRoom.Picture = SnCloudFileReferenceObject.FromProtoValue(fileResponse);
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

                chatRoom.Background = SnCloudFileReferenceObject.FromProtoValue(fileResponse);
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
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to delete the chat.");
        }
        else if (chatRoom.Type == ChatRoomType.DirectMessage && !await crs.IsChatMember(chatRoom.Id, accountId))
            return StatusCode(403, "You need be part of the DM to update the chat.");
        else if (chatRoom.AccountId != accountId)
            return StatusCode(403, "You need be the owner to update the chat.");

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
    public async Task<ActionResult<SnChatMember>> GetRoomIdentity(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser)
            return Unauthorized();

        var member = await db.ChatMembers
            .Where(m => m.AccountId == Guid.Parse(currentUser.Id) && m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null)
            return NotFound();

        return Ok(await crs.LoadMemberAccount(member));
    }

    [HttpGet("{roomId:guid}/members/online")]
    public async Task<ActionResult<int>> GetOnlineUsersCount(Guid roomId)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;

        var room = await db.ChatRooms
            .FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (currentUser is null) return Unauthorized();
            var member = await db.ChatMembers
                .Where(m => m.ChatRoomId == roomId && m.AccountId == Guid.Parse(currentUser.Id) && m.JoinedAt != null &&
                            m.LeaveAt == null)
                .FirstOrDefaultAsync();
            if (member is null)
                return StatusCode(403, "You need to be a member to see online count of private chat room.");
        }

        var members = await db.ChatMembers
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Select(m => m.AccountId)
            .ToListAsync();

        var memberStatuses = await remoteAccountsHelper.GetAccountStatusBatch(members);

        var onlineCount = memberStatuses.Count(s => s.Value.IsOnline);

        return Ok(onlineCount);
    }

    [HttpGet("{roomId:guid}/members")]
    public async Task<ActionResult<List<SnChatMember>>> ListMembers(
        Guid roomId,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] bool withStatus = false
    )
    {
        var currentUser = HttpContext.Items["CurrentUser"] as Account;
        Guid? accountId = currentUser is not null ? Guid.Parse(currentUser.Id) : null;

        var room = await db.ChatRooms
            .FirstOrDefaultAsync(r => r.Id == roomId);
        if (room is null) return NotFound();

        if (!room.IsPublic)
        {
            if (accountId is null) return Unauthorized();
            var member = await db.ChatMembers
                .Where(m => m.ChatRoomId == roomId && m.AccountId == accountId && m.LeaveAt == null)
                .FirstOrDefaultAsync();
            if (member is null) return StatusCode(403, "You need to be a member to see members of private chat room.");
        }

        // The query should include the unjoined ones, to show the invites.
        var query = db.ChatMembers
            .Where(m => m.ChatRoomId == roomId)
            .Where(m => m.LeaveAt == null);

        if (withStatus)
        {
            var members = await query
                .OrderBy(m => m.JoinedAt)
                .ToListAsync();

            var memberStatuses = await remoteAccountsHelper.GetAccountStatusBatch(
                members.Select(m => m.AccountId).ToList()
            );

            members = members
                .Select(m =>
                {
                    m.Status = memberStatuses.TryGetValue(m.AccountId, out var s) ? s : null;
                    return m;
                })
                .OrderByDescending(m => m.Status?.IsOnline ?? false)
                .ToList();

            var total = members.Count;
            Response.Headers.Append("X-Total", total.ToString());

            var result = members.Skip(offset).Take(take).ToList();

            members = await crs.LoadMemberAccounts(result);

            return Ok(members.Where(m => m.Account is not null).ToList());
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
    public async Task<ActionResult<SnChatMember>> InviteMember(Guid roomId, [FromBody] ChatMemberRequest request)
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

        if (relationship?.Relationship is { Status: -100 })
            return StatusCode(403, "You cannot invite a user that blocked you.");

        var chatRoom = await db.ChatRooms
            .Where(p => p.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        var operatorMember = await db.ChatMembers
            .Where(p => p.AccountId == accountId && p.ChatRoomId == chatRoom.Id)
            .FirstOrDefaultAsync();
        if (operatorMember is null)
            return StatusCode(403, "You need to be a part of chat to invite member to the chat.");

        // Handle realm-owned chat rooms
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to invite members to this chat.");
        }
        else if (chatRoom.Type == ChatRoomType.DirectMessage && !await crs.IsChatMember(chatRoom.Id, accountId))
            return StatusCode(403, "You need be part of the DM to invite member to the chat.");
        else if (chatRoom.AccountId != accountId)
            return StatusCode(403, "You need be the owner to invite member to this chat.");

        var existingMember = await db.ChatMembers
            .Where(m => m.AccountId == request.RelatedUserId)
            .Where(m => m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (existingMember != null)
        {
            existingMember.InvitedById = operatorMember.Id;
            existingMember.LeaveAt = null;
            existingMember.JoinedAt = null;
            db.ChatMembers.Update(existingMember);
            await db.SaveChangesAsync();
            await SendInviteNotify(existingMember, currentUser);

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

            return Ok(existingMember);
        }

        var newMember = new SnChatMember
        {
            InvitedById = operatorMember.Id,
            AccountId = Guid.Parse(relatedUser.Id),
            ChatRoomId = roomId,
        };

        db.ChatMembers.Add(newMember);
        await db.SaveChangesAsync();

        newMember.ChatRoom = chatRoom;
        await SendInviteNotify(newMember, currentUser);

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
    public async Task<ActionResult<List<SnChatMember>>> ListChatInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not Shared.Proto.Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var members = await db.ChatMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt == null)
            .Include(e => e.ChatRoom)
            .ToListAsync();

        var chatRooms = members.Select(m => m.ChatRoom).ToList();
        chatRooms = await crs.LoadDirectMessageMembers(chatRooms, accountId);
        chatRooms = await crs.LoadChatRealms(chatRooms);
        var directMembers =
            chatRooms.ToDictionary(c => c.Id, c => c.Members);

        foreach (var member in members.Where(member => member.ChatRoom.Type == ChatRoomType.DirectMessage))
            member.ChatRoom.Members = directMembers[member.ChatRoom.Id];

        return Ok(await crs.LoadMemberAccounts(members));
    }

    [HttpPost("invites/{roomId:guid}/accept")]
    [Authorize]
    public async Task<ActionResult<SnChatRoom>> AcceptChatInvite(Guid roomId)
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

        db.ChatMembers.Remove(member);
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
    public async Task<ActionResult<SnChatMember>> UpdateChatMemberNotify(
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
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
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

    public class ChatTimeoutRequest
    {
        [MaxLength(4096)] public string? Reason { get; set; }
        public Instant TimeoutUntil { get; set; }
    }

    [HttpPost("{roomId:guid}/members/{memberId:guid}/timeout")]
    [Authorize]
    public async Task<ActionResult> TimeoutChatMember(Guid roomId, Guid memberId, [FromBody] ChatTimeoutRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var now = SystemClock.Instance.GetCurrentInstant();
        if (now >= request.TimeoutUntil)
            return BadRequest("Timeout can only until a time in the future.");

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        var operatorMember = await db.ChatMembers
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.ChatRoomId == chatRoom.Id);
        if (operatorMember is null) return BadRequest("You have not joined this chat room.");

        // Check if the chat room is owned by a realm
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to timeout members.");
        }
        else if (chatRoom.Type == ChatRoomType.DirectMessage)
            return BadRequest("You cannot timeout member in a direct message.");
        else if (chatRoom.AccountId != accountId)
            return StatusCode(403, "You need be the owner to timeout member in the chat.");

        // Find the target member
        var member = await db.ChatMembers
            .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.TimeoutCause = new ChatTimeoutCause
        {
            Reason = request.Reason,
            SenderId = operatorMember.Id,
            Type = ChatTimeoutCauseType.ByModerator,
            Since = now
        };
        member.TimeoutUntil = request.TimeoutUntil;
        db.Update(member);
        await db.SaveChangesAsync();
        _ = crs.PurgeRoomMembersCache(roomId);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.timeout",
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

    [HttpDelete("{roomId:guid}/members/{memberId:guid}/timeout")]
    [Authorize]
    public async Task<ActionResult> RemoveChatMemberTimeout(Guid roomId, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Check if the chat room is owned by a realm
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to remove members.");
        }
        else if (chatRoom.Type == ChatRoomType.DirectMessage && !await crs.IsChatMember(chatRoom.Id, accountId))
            return StatusCode(403, "You need be part of the DM to update the chat.");
        else if (chatRoom.AccountId != accountId)
            return StatusCode(403, "You need be the owner to update the chat.");

        // Find the target member
        var member = await db.ChatMembers
            .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.TimeoutCause = null;
        member.TimeoutUntil = null;
        db.Update(member);
        await db.SaveChangesAsync();
        _ = crs.PurgeRoomMembersCache(roomId);

        _ = als.CreateActionLogAsync(new CreateActionLogRequest
        {
            Action = "chatrooms.timeout.remove",
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

    [HttpDelete("{roomId:guid}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveChatMember(Guid roomId, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Check if the chat room is owned by a realm
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to remove members.");
        }
        else if (chatRoom.Type == ChatRoomType.DirectMessage && !await crs.IsChatMember(chatRoom.Id, accountId))
            return StatusCode(403, "You need be part of the DM to update the chat.");
        else if (chatRoom.AccountId != accountId)
            return StatusCode(403, "You need be the owner to update the chat.");

        // Find the target member
        var member = await db.ChatMembers
            .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

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
    public async Task<ActionResult<SnChatRoom>> JoinChatRoom(Guid roomId)
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
        {
            if (existingMember.LeaveAt == null)
                return BadRequest("You are already a member of this chat room.");

            existingMember.LeaveAt = null;
            db.Update(existingMember);
            await db.SaveChangesAsync();
            _ = crs.PurgeRoomMembersCache(roomId);

            return Ok(existingMember);
        }

        var newMember = new SnChatMember
        {
            AccountId = Guid.Parse(currentUser.Id),
            ChatRoomId = roomId,
            JoinedAt = SystemClock.Instance.GetCurrentInstant()
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
        var accountId = Guid.Parse(currentUser.Id);

        var chat = await db.ChatRooms.FirstOrDefaultAsync(c => c.Id == roomId);
        if (chat is null) return NotFound();
        if (chat.AccountId == accountId)
            return BadRequest("You cannot leave you own chat room");

        var member = await db.ChatMembers
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Where(m => m.AccountId == Guid.Parse(currentUser.Id))
            .Where(m => m.ChatRoomId == roomId)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(member);
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

    private async Task SendInviteNotify(SnChatMember member, Account sender)
    {
        var account = await accounts.GetAccountAsync(new GetAccountRequest { Id = member.AccountId.ToString() });
        var title = "Chat Invite";
        var body = member.ChatRoom.Type == ChatRoomType.DirectMessage
            ? $"{sender.Nick} sent you a direct message"
            : $"You have been invited to {member.ChatRoom.Name ?? "Unnamed"}";


        await pusher.SendPushNotificationToUserAsync(
            new SendPushNotificationToUserRequest
            {
                UserId = account.Id,
                Notification = new PushNotification
                {
                    Topic = "invites.chats",
                    Title = title,
                    Body = body,
                    IsSavable = true,
                    Meta = InfraObjectCoder.ConvertObjectToByteString(new
                    {
                        room_id = member.ChatRoomId
                    })
                }
            }
        );
    }
}
