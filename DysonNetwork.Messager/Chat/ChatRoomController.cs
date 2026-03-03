using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using NodaTime;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Messager.Chat;

[ApiController]
[Route("/api/chat")]
public class ChatRoomController(
    AppDatabase db,
    ChatRoomService crs,
    ChatService cs,
    RemoteRealmService rs,
    DyAccountService.DyAccountServiceClient accounts,
    DyFileService.DyFileServiceClient files,
    DyActionLogService.DyActionLogServiceClient als,
    DyRingService.DyRingServiceClient pusher,
    RemoteAccountService remoteAccountsHelper,
    ILocalizationService localization
) : ControllerBase
{
    private const string DefaultMlsCiphersuite = "MLS_128_DHKEMX25519_AES128GCM_SHA256_Ed25519";
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

        if (HttpContext.Items["CurrentUser"] is DyAccount currentUser)
            chatRoom = await crs.LoadDirectMessageMembers(chatRoom, Guid.Parse(currentUser.Id));

        return Ok(chatRoom);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnChatRoom>>> ListJoinedChatRooms()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
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
        public ChatRoomEncryptionMode? EncryptionMode { get; set; }
    }

    [HttpPost("direct")]
    [Authorize]
    public async Task<ActionResult<SnChatRoom>> CreateDirectMessage([FromBody] DirectMessageRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var relatedUser = await accounts.GetAccountAsync(
            new DyGetAccountRequest { Id = request.RelatedUserId.ToString() }
        );
        if (relatedUser is null)
            return BadRequest("Related user was not found");

        var hasBlocked = await accounts.HasRelationshipAsync(new DyGetRelationshipRequest()
        {
            AccountId = currentUser.Id,
            RelatedId = request.RelatedUserId.ToString(),
            Status = -100
        });
        if (hasBlocked?.Value ?? false)
            return StatusCode(403, "You cannot create direct message with a user that blocked you.");

        // Check if DM already exists between these users in the same encryption mode.
        // This allows one plaintext DM and one encrypted DM to coexist for the same pair.
        var requestedMode = request.EncryptionMode ?? ChatRoomEncryptionMode.None;
        if (!IsEncryptionModeValidForRoomType(ChatRoomType.DirectMessage, requestedMode))
            return Conflict(new
            {
                code = "chat.e2ee_mode_invalid_for_room",
                error = "Invalid encryption mode for direct message room."
            });
        if (!IsNewEncryptedModeAllowed(requestedMode))
            return Conflict(new
            {
                code = "chat.e2ee_legacy_mode_forbidden",
                error = "Legacy encrypted room modes are not allowed for new rooms. Use E2eeMls."
            });

        var existingDm = await db.ChatRooms
            .Include(c => c.Members)
            .Where(c => c.Type == ChatRoomType.DirectMessage && c.Members.Count == 2)
            .Where(c => c.EncryptionMode == requestedMode)
            .Where(c => c.Members.Any(m => m.AccountId == Guid.Parse(currentUser.Id)))
            .Where(c => c.Members.Any(m => m.AccountId == request.RelatedUserId))
            .FirstOrDefaultAsync();

        if (existingDm != null)
            return BadRequest("You already have a DM with this user.");

        // Create new DM chat room
        var encryptionMode = requestedMode;
        var dmRoom = new SnChatRoom
        {
            Type = ChatRoomType.DirectMessage,
            IsPublic = false,
            AccountId = accountId,
            EncryptionMode = encryptionMode,
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

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.create",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(dmRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
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
        public ChatRoomEncryptionMode? EncryptionMode { get; set; }
        public Dictionary<string, object>? E2eePolicy { get; set; }
    }

    private static bool IsEncryptionModeValidForRoomType(ChatRoomType roomType, ChatRoomEncryptionMode mode)
    {
        return roomType switch
        {
            ChatRoomType.DirectMessage => mode is ChatRoomEncryptionMode.None or ChatRoomEncryptionMode.E2eeMls,
            ChatRoomType.Group => mode is ChatRoomEncryptionMode.None or ChatRoomEncryptionMode.E2eeMls,
            _ => false
        };
    }

    private static bool IsNewEncryptedModeAllowed(ChatRoomEncryptionMode mode)
    {
        return mode is ChatRoomEncryptionMode.None or ChatRoomEncryptionMode.E2eeMls;
    }

    private async Task EmitEncryptionMembershipChangedEventAsync(
        SnChatRoom room,
        SnChatMember actor,
        Guid changedMemberId,
        string reason
    )
    {
        if (room.EncryptionMode == ChatRoomEncryptionMode.E2eeMls)
        {
            var epochHint = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await cs.SendMlsEpochChangedSystemMessageAsync(room, actor, epochHint, reason);
        }
    }

    [HttpPost]
    [Authorize]
    [AskPermission("chat.create")]
    public async Task<ActionResult<SnChatRoom>> CreateChatRoom(ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
            EncryptionMode = request.EncryptionMode ?? ChatRoomEncryptionMode.None,
            E2eePolicy = request.E2eePolicy,
            Members = new List<SnChatMember>
            {
                new()
                {
                    AccountId = accountId,
                    JoinedAt = SystemClock.Instance.GetCurrentInstant()
                }
            }
        };
        if (!IsEncryptionModeValidForRoomType(ChatRoomType.Group, chatRoom.EncryptionMode))
            return Conflict(new
            {
                code = "chat.e2ee_mode_invalid_for_room",
                error = "Invalid encryption mode for group room."
            });
        if (!IsNewEncryptedModeAllowed(chatRoom.EncryptionMode))
            return Conflict(new
            {
                code = "chat.e2ee_legacy_mode_forbidden",
                error = "Legacy encrypted room modes are not allowed for new rooms. Use E2eeMls."
            });

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
                var fileResponse = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
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
                var fileResponse = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
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

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.create",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return Ok(chatRoom);
    }


    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<SnChatRoom>> UpdateChatRoom(Guid id, [FromBody] ChatRoomRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Authorization
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to update this chat.");
        }
        else
        {
            // Non-realm chat: permissions depend on room type
            switch (chatRoom.Type)
            {
                case ChatRoomType.DirectMessage:
                    if (!await crs.IsChatMember(chatRoom.Id, accountId))
                        return StatusCode(403, "You need be part of the DM to update this chat.");
                    break;
                case ChatRoomType.Group:
                    if (chatRoom.AccountId != accountId)
                        return StatusCode(403, "You need be the owner to update for this chat.");
                    break;
            }
        }

        var previousName = chatRoom.Name;
        var previousDescription = chatRoom.Description;
        var previousIsCommunity = chatRoom.IsCommunity;
        var previousIsPublic = chatRoom.IsPublic;
        var previousRealmId = chatRoom.RealmId;
        var previousPictureId = chatRoom.Picture?.Id;
        var previousBackgroundId = chatRoom.Background?.Id;

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
                var fileResponse = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
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
                var fileResponse = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
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
        if (request.EncryptionMode is not null)
            return Conflict(new
            {
                code = "chat.mls_enable_endpoint_required",
                error = "Use POST /api/chat/{id}/mls/enable to enable encryption. Encryption cannot be disabled."
            });
        if (request.E2eePolicy is not null)
            chatRoom.E2eePolicy = request.E2eePolicy;

        if (!IsEncryptionModeValidForRoomType(chatRoom.Type, chatRoom.EncryptionMode))
            return Conflict(new
            {
                code = "chat.e2ee_mode_invalid_for_room",
                error = "Invalid encryption mode for this room type."
            });

        db.ChatRooms.Update(chatRoom);
        await db.SaveChangesAsync();

        var changes = new Dictionary<string, object>();
        if (previousName != chatRoom.Name)
            changes["name"] = new Dictionary<string, object?>
            {
                ["old"] = previousName,
                ["new"] = chatRoom.Name
            };
        if (previousDescription != chatRoom.Description)
            changes["description"] = new Dictionary<string, object?>
            {
                ["old"] = previousDescription,
                ["new"] = chatRoom.Description
            };
        if (previousIsCommunity != chatRoom.IsCommunity)
            changes["is_community"] = new Dictionary<string, object>
            {
                ["old"] = previousIsCommunity,
                ["new"] = chatRoom.IsCommunity
            };
        if (previousIsPublic != chatRoom.IsPublic)
            changes["is_public"] = new Dictionary<string, object>
            {
                ["old"] = previousIsPublic,
                ["new"] = chatRoom.IsPublic
            };
        if (previousRealmId != chatRoom.RealmId)
            changes["realm_id"] = new Dictionary<string, object?>
            {
                ["old"] = previousRealmId?.ToString(),
                ["new"] = chatRoom.RealmId?.ToString()
            };
        if (previousPictureId != chatRoom.Picture?.Id)
            changes["picture_id"] = new Dictionary<string, object?>
            {
                ["old"] = previousPictureId,
                ["new"] = chatRoom.Picture?.Id
            };
        if (previousBackgroundId != chatRoom.Background?.Id)
            changes["background_id"] = new Dictionary<string, object?>
            {
                ["old"] = previousBackgroundId,
                ["new"] = chatRoom.Background?.Id
            };
        if (changes.Count > 0)
        {
            var operatorMember = await db.ChatMembers
                .Where(m => m.ChatRoomId == chatRoom.Id && m.AccountId == accountId)
                .Where(m => m.JoinedAt != null && m.LeaveAt == null)
                .FirstOrDefaultAsync();

            if (operatorMember is not null)
                await cs.SendChatInfoUpdatedSystemMessageAsync(chatRoom, operatorMember, changes);
        }

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.update",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return Ok(chatRoom);
    }

    public class EnableE2eeRequest
    {
        public ChatRoomEncryptionMode? EncryptionMode { get; set; }
    }

    [HttpPost("{id:guid}/e2ee/enable")]
    [Authorize]
    public async Task<ActionResult<SnChatRoom>> EnableE2Ee(Guid id, [FromBody] EnableE2eeRequest? request)
    {
        return StatusCode(410, new
        {
            code = "chat.e2ee_legacy_endpoint_removed",
            error = "Legacy e2ee/enable endpoint is removed. Use POST /api/chat/{id}/mls/enable."
        });
    }

    public class EnableMlsRequest
    {
        [MaxLength(256)] public string? MlsGroupId { get; set; }
        public Dictionary<string, object>? E2eePolicy { get; set; }
    }

    [HttpPost("{id:guid}/mls/enable")]
    [Authorize]
    public async Task<ActionResult<SnChatRoom>> EnableMls(Guid id, [FromBody] EnableMlsRequest? request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to enable MLS for this chat.");
        }
        else
        {
            switch (chatRoom.Type)
            {
                case ChatRoomType.DirectMessage:
                    if (!await crs.IsChatMember(chatRoom.Id, accountId))
                        return StatusCode(403, "You need be part of the DM to enable MLS for this chat.");
                    break;
                case ChatRoomType.Group:
                    if (chatRoom.AccountId != accountId)
                        return StatusCode(403, "You need be the owner to enable MLS for this chat.");
                    break;
            }
        }

        if (chatRoom.EncryptionMode != ChatRoomEncryptionMode.None)
            return Conflict(new
            {
                code = "chat.e2ee_already_enabled",
                error = "Encryption is already enabled for this room and cannot be disabled."
            });

        chatRoom.EncryptionMode = ChatRoomEncryptionMode.E2eeMls;
        chatRoom.MlsGroupId = string.IsNullOrWhiteSpace(request?.MlsGroupId)
            ? $"chat:{chatRoom.Id}"
            : request!.MlsGroupId;
        chatRoom.E2eePolicy ??= new Dictionary<string, object>();
        if (!chatRoom.E2eePolicy.ContainsKey("ciphersuite"))
            chatRoom.E2eePolicy["ciphersuite"] = DefaultMlsCiphersuite;
        if (request?.E2eePolicy is not null)
            foreach (var kv in request.E2eePolicy)
                chatRoom.E2eePolicy[kv.Key] = kv.Value;

        db.ChatRooms.Update(chatRoom);
        await db.SaveChangesAsync();

        var operatorMember = await db.ChatMembers
            .Where(m => m.ChatRoomId == chatRoom.Id && m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (operatorMember is not null)
            await cs.SendE2eeEnabledSystemMessageAsync(chatRoom, operatorMember, chatRoom.EncryptionMode, chatRoom.MlsGroupId);

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.mls.enable",
            Meta =
            {
                { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) },
                { "encryption_mode", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.EncryptionMode.ToString()) },
                { "mls_group_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.MlsGroupId) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return Ok(chatRoom);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteChatRoom(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(e => e.Id == id)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Authorization
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to delete for this chat.");
        }
        else
        {
            switch (chatRoom.Type)
            {
                case ChatRoomType.DirectMessage:
                    if (!await crs.IsChatMember(chatRoom.Id, accountId))
                        return StatusCode(403, "You need be part of the DM to delete this chat.");
                    break;
                case ChatRoomType.Group:
                    if (chatRoom.AccountId != accountId)
                        return StatusCode(403, "You need be the owner to delete for this chat.");
                    break;
            }
        }

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

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.delete",
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return NoContent();
    }

    [HttpGet("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult<SnChatMember>> GetRoomIdentity(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
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
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;

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
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Get related user account
        var relatedUser =
            await accounts.GetAccountAsync(new DyGetAccountRequest { Id = request.RelatedUserId.ToString() });
        if (relatedUser == null) return BadRequest("Related user was not found");

        // Check if the user has blocked the current user
        var relationship = await accounts.GetRelationshipAsync(new DyGetRelationshipRequest
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

        // Authorization
        if (chatRoom.RealmId is not null)
        {
            // Realm-owned chat: only moderators can enable E2EE
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to invite member for this chat.");
        }
        else
        {
            // Non-realm chat: permissions depend on room type
            switch (chatRoom.Type)
            {
                case ChatRoomType.DirectMessage:
                    if (!await crs.IsChatMember(chatRoom.Id, accountId))
                        return StatusCode(403, "You need be part of the DM to invite member to this chat.");
                    break;
                case ChatRoomType.Group:
                    if (chatRoom.AccountId != accountId)
                        return StatusCode(403, "You need be the owner to invite member for this chat.");
                    break;
            }
        }

        if (chatRoom.Type == ChatRoomType.DirectMessage &&
            chatRoom.EncryptionMode == ChatRoomEncryptionMode.E2eeMls)
        {
            var joinedMemberCount = await db.ChatMembers
                .Where(m => m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
                .CountAsync();
            if (joinedMemberCount >= 2)
                return Conflict(new
                {
                    code = "chat.e2ee_dm_member_limit",
                    error = "MLS direct-message rooms only support two active members."
                });
        }

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

            _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
            {
                Action = "chatrooms.invite",
                Meta =
                {
                    { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) },
                    { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(relatedUser.Id.ToString()) }
                },
                AccountId = currentUser.Id,
                UserAgent = Request.Headers.UserAgent,
                IpAddress = Request.GetClientIpAddress()
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

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.invite",
            Meta =
            {
                { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(chatRoom.Id.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(relatedUser.Id.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return Ok(newMember);
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<SnChatMember>>> ListChatInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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

        var memberRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (memberRoom is not null)
        {
            await cs.SendMemberJoinedSystemMessageAsync(memberRoom, member);
            await EmitEncryptionMembershipChangedEventAsync(memberRoom, member, member.AccountId, "member_joined");
        }

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = ActionLogType.ChatroomJoin,
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return Ok(member);
    }

    [HttpPost("invites/{roomId:guid}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineChatInvite(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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

        // Authorization
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to timeout member for this chat.");
        }
        else
        {
            // Non-realm chat: permissions depend on room type
            switch (chatRoom.Type)
            {
                case ChatRoomType.DirectMessage:
                    return BadRequest("You cannot timeout member in DM");
                case ChatRoomType.Group:
                    if (chatRoom.AccountId != accountId)
                        return StatusCode(403, "You need be the owner to timeout member in this chat.");
                    break;
            }
        }

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

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.timeout",
            Meta =
            {
                { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(memberId.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return NoContent();
    }

    [HttpDelete("{roomId:guid}/members/{memberId:guid}/timeout")]
    [Authorize]
    public async Task<ActionResult> RemoveChatMemberTimeout(Guid roomId, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Authorization
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to cancel timeout in this chat.");
        }
        else
        {
            // Non-realm chat: permissions depend on room type
            switch (chatRoom.Type)
            {
                case ChatRoomType.DirectMessage:
                    if (!await crs.IsChatMember(chatRoom.Id, accountId))
                        return StatusCode(403, "You need be part of the DM to cancel timeout in this chat.");
                    break;
                case ChatRoomType.Group:
                    if (chatRoom.AccountId != accountId)
                        return StatusCode(403, "You need be the owner to cancel timeout for this chat.");
                    break;
            }
        }

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

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.timeout.remove",
            Meta =
            {
                { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(memberId.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return NoContent();
    }

    [HttpDelete("{roomId:guid}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveChatMember(Guid roomId, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var chatRoom = await db.ChatRooms
            .Where(r => r.Id == roomId)
            .FirstOrDefaultAsync();
        if (chatRoom is null) return NotFound();

        // Authorization
        if (chatRoom.RealmId is not null)
        {
            if (!await rs.IsMemberWithRole(chatRoom.RealmId.Value, accountId, [RealmMemberRole.Moderator]))
                return StatusCode(403, "You need at least be a realm moderator to remove member for this chat.");
        }
        else
        {
            switch (chatRoom.Type)
            {
                case ChatRoomType.DirectMessage:
                    if (!await crs.IsChatMember(chatRoom.Id, accountId))
                        return StatusCode(403, "You need be part of the DM to remove member for this chat.");
                    break;
                case ChatRoomType.Group:
                    if (chatRoom.AccountId != accountId)
                        return StatusCode(403, "You need be the owner to remove member for this chat.");
                    break;
            }
        }

        // Find the target member
        var member = await db.ChatMembers
            .Where(m => m.AccountId == memberId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        var operatorMember = await db.ChatMembers
            .Where(m => m.ChatRoomId == roomId && m.AccountId == accountId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        _ = crs.PurgeRoomMembersCache(roomId);

        if (operatorMember is not null)
        {
            await cs.SendMemberLeftSystemMessageAsync(chatRoom, member, operatorMember);
            await EmitEncryptionMembershipChangedEventAsync(chatRoom, operatorMember, member.AccountId, "member_removed");
        }

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = "chatrooms.kick",
            Meta =
            {
                { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) },
                { "account_id", Google.Protobuf.WellKnownTypes.Value.ForString(memberId.ToString()) }
            },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return NoContent();
    }


    [HttpPost("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult<SnChatRoom>> JoinChatRoom(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();

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
            existingMember.JoinedAt = SystemClock.Instance.GetCurrentInstant();
            db.Update(existingMember);
            await db.SaveChangesAsync();
            _ = crs.PurgeRoomMembersCache(roomId);
            await cs.SendMemberJoinedSystemMessageAsync(chatRoom, existingMember);
            await EmitEncryptionMembershipChangedEventAsync(chatRoom, existingMember, existingMember.AccountId, "member_joined");

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
        await cs.SendMemberJoinedSystemMessageAsync(chatRoom, newMember);
        await EmitEncryptionMembershipChangedEventAsync(chatRoom, newMember, newMember.AccountId, "member_joined");

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = ActionLogType.ChatroomJoin,
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return Ok(chatRoom);
    }

    [HttpDelete("{roomId:guid}/members/me")]
    [Authorize]
    public async Task<ActionResult> LeaveChat(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
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
        await cs.SendMemberLeftSystemMessageAsync(chat, member);
        await EmitEncryptionMembershipChangedEventAsync(chat, member, member.AccountId, "member_left");

        _ = als.CreateActionLogAsync(new DyCreateActionLogRequest
        {
            Action = ActionLogType.ChatroomLeave,
            Meta = { { "chatroom_id", Google.Protobuf.WellKnownTypes.Value.ForString(roomId.ToString()) } },
            AccountId = currentUser.Id,
            UserAgent = Request.Headers.UserAgent,
            IpAddress = Request.GetClientIpAddress()
        });

        return NoContent();
    }

    private async Task SendInviteNotify(SnChatMember member, DyAccount sender)
    {
        var account = await accounts.GetAccountAsync(new DyGetAccountRequest { Id = member.AccountId.ToString() });
        var locale = account.Language;
        
        var title = localization.Get("chatInviteTitle", locale);
        var body = member.ChatRoom.Type == ChatRoomType.DirectMessage
            ? localization.Get("chatInviteBodyDirectMessage", locale, new { senderNick = sender.Nick })
            : localization.Get("chatInviteBodyGroupInvite", locale, new { roomName = member.ChatRoom.Name ?? "Unnamed" });


        await pusher.SendPushNotificationToUserAsync(
            new DySendPushNotificationToUserRequest
            {
                UserId = account.Id,
                Notification = new DyPushNotification
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
