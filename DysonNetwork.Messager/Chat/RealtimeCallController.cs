using DysonNetwork.Messager.Chat.Realtime;
using DysonNetwork.Messager.Models;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

public class RealtimeChatConfiguration
{
    public string Endpoint { get; set; } = null!;
}

[ApiController]
[Route("/api/chat/realtime")]
public class RealtimeCallController(
    IConfiguration configuration,
    AppDatabase db,
    ChatService cs,
    ChatRoomService crs,
    IRealtimeService realtime,
    ILocalizationService localization,
    DyAccountService.DyAccountServiceClient accounts
) : ControllerBase
{
    private readonly RealtimeChatConfiguration _config = configuration
        .GetSection("RealtimeChat")
        .Get<RealtimeChatConfiguration>()!;

    [HttpGet("{roomId:guid}/participants")]
    [Authorize]
    public async Task<ActionResult<List<CallParticipant>>> GetParticipants(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await GetJoinedMemberAsync(roomId, accountId);
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member to view participants.", forbidden: true));

        var call = await EnsureRealtimeCallAsync(roomId, member);
        if (string.IsNullOrWhiteSpace(call.SessionId))
            return BadRequest(new ApiError { Code = "CHAT_CALL_SESSION_NOT_CONFIGURED", Message = "Call session is not properly configured.", Status = 400 });

        var roomParticipants = await SyncProviderParticipantsAsync(call.SessionId);
        var participants = await BuildParticipantsAsync(roomId, roomParticipants);

        return Ok(participants);
    }

    [HttpPost("{roomId:guid}/kick/{targetAccountId:guid}")]
    [AskPermission(PermissionKeys.ChatCallKick)]
    [Authorize]
    public async Task<IActionResult> KickParticipant(
        Guid roomId,
        Guid targetAccountId,
        [FromBody] KickParticipantRequest? request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await GetJoinedMemberAsync(roomId, accountId, includeRoom: true);
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member.", forbidden: true));

        var call = await EnsureRealtimeCallAsync(roomId, member);
        var roomOwnerId = call.Room.AccountId ?? member.ChatRoom.AccountId;
        var isAdmin =
            member.AccountId == roomOwnerId || call.Room.Type == ChatRoomType.DirectMessage;
        if (!isAdmin)
            return StatusCode(403, ApiError.Unauthorized("Only room admin can kick participants.", forbidden: true));

        var targetMember = await db
            .ChatMembers.Where(m =>
                m.AccountId == targetAccountId
                && m.ChatRoomId == roomId
                && m.JoinedAt != null
                && m.LeaveAt == null
            )
            .FirstOrDefaultAsync();
        if (targetMember is null)
            return NotFound(ApiError.NotFound("member", message: "Target member not found.", code: "CHAT_CALL_TARGET_NOT_FOUND"));

        if (targetMember.AccountId == roomOwnerId)
            return BadRequest(new ApiError { Code = "CHAT_CALL_CANNOT_KICK_OWNER", Message = "Cannot kick the room owner.", Status = 400 });

        var participantRemoved = false;
        if (
            realtime is LiveKitRealtimeService livekitService
            && !string.IsNullOrWhiteSpace(call.SessionId)
        )
        {
            var participants = await livekitService.SyncParticipantsAsync(call.SessionId);
            var targetParticipant = participants.FirstOrDefault(p =>
                p.AccountId == targetAccountId
            );
            if (targetParticipant != null)
            {
                await livekitService.KickParticipantAsync(
                    call.SessionId,
                    targetParticipant.Identity
                );
                participantRemoved = true;
            }
        }

        if (request?.BanDurationMinutes > 0)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            targetMember.TimeoutUntil = now.Plus(
                Duration.FromMinutes(request.BanDurationMinutes.Value)
            );
            targetMember.TimeoutCause = new ChatTimeoutCause
            {
                Type = ChatTimeoutCauseType.ByModerator,
                Reason = request.Reason ?? "Kicked from call",
                SenderId = accountId,
                Since = now,
            };
            await db.SaveChangesAsync();
        }

        if (participantRemoved)
        {
            var memberName = targetMember.Nick ?? targetMember.Account?.Nick ?? "Someone";
            var operatorName = member.Nick ?? member.Account?.Nick ?? "Moderator";
            await cs.SendSystemMessageAsync(
                call.Room,
                member,
                "system.call.member.left",
                $"{memberName} was removed from the call by {operatorName}.",
                new Dictionary<string, object>
                {
                    ["event"] = "call_member_left",
                    ["reason"] = "removed",
                    ["call_id"] = call.Id,
                    ["member_id"] = targetMember.Id,
                    ["account_id"] = targetMember.AccountId,
                    ["operator_member_id"] = member.Id,
                    ["operator_account_id"] = member.AccountId,
                }
            );
        }

        return NoContent();
    }

    [HttpPost("{roomId:guid}/mute/{targetAccountId:guid}")]
    [AskPermission(PermissionKeys.ChatCallMute)]
    [Authorize]
    public async Task<IActionResult> MuteParticipant(Guid roomId, Guid targetAccountId)
    {
        return await ToggleMuteParticipant(roomId, targetAccountId, true);
    }

    [HttpPost("{roomId:guid}/unmute/{targetAccountId:guid}")]
    [AskPermission(PermissionKeys.ChatCallMute)]
    [Authorize]
    public async Task<IActionResult> UnmuteParticipant(Guid roomId, Guid targetAccountId)
    {
        return await ToggleMuteParticipant(roomId, targetAccountId, false);
    }

    [HttpGet("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> GetOngoingCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await GetJoinedMemberAsync(roomId, accountId);
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member to view call status.", forbidden: true));

        var call = await EnsureRealtimeCallAsync(roomId, member);
        call.Sender = await crs.LoadMemberAccount(call.Sender);
        return Ok(call);
    }

    [HttpGet("{roomId:guid}/join")]
    [Authorize]
    public async Task<ActionResult<JoinCallResponse>> JoinCall(
        Guid roomId,
        [FromQuery(Name = "tool")] string? tool = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await GetJoinedMemberAsync(roomId, accountId, includeRoom: true);

        var now = SystemClock.Instance.GetCurrentInstant();
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member to join a call.", forbidden: true));
        if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            return StatusCode(403, ApiError.Unauthorized("You has been timed out in this chat.", forbidden: true));

        var call = await EnsureRealtimeCallAsync(roomId, member);
        if (string.IsNullOrWhiteSpace(call.SessionId))
            return BadRequest(new ApiError { Code = "CHAT_CALL_SESSION_NOT_CONFIGURED", Message = "Call session is not properly configured.", Status = 400 });

        var roomParticipants = await SyncProviderParticipantsAsync(call.SessionId);
        var defaultIdentity = realtime.GetParticipantIdentity(currentUser);
        var participantIdentity = realtime.GetParticipantIdentity(currentUser, tool);
        var isTool = participantIdentity != defaultIdentity;
        var alreadyInCall = roomParticipants.Any(p => p.Identity == participantIdentity);

        var roomOwnerId = call.Room.AccountId ?? member.ChatRoom.AccountId;
        var isAdmin =
            member.AccountId == roomOwnerId || call.Room.Type == ChatRoomType.DirectMessage;
        var userToken = realtime.GetUserToken(currentUser, call.SessionId, isAdmin, tool);

        var endpoint =
            _config.Endpoint
            ?? throw new InvalidOperationException("LiveKit endpoint configuration is missing");

        var participants = await BuildParticipantsAsync(roomId, roomParticipants);

        if (!isTool && !alreadyInCall)
        {
            var memberName = member.Nick ?? member.Account?.Nick ?? "Someone";
            await cs.SendSystemMessageAsync(
                call.Room,
                member,
                "system.call.member.joined",
                $"{memberName} joined the call.",
                new Dictionary<string, object>
                {
                    ["event"] = "call_member_joined",
                    ["call_id"] = call.Id,
                    ["member_id"] = member.Id,
                    ["account_id"] = member.AccountId,
                }
            );
        }

        return Ok(
            new JoinCallResponse
            {
                Provider = realtime.ProviderName,
                Endpoint = endpoint,
                Token = userToken,
                CallId = call.Id,
                RoomName = call.SessionId,
                RoomTitle =
                    call.Room is { Type: ChatRoomType.DirectMessage, Name: null } ? "DM"
                    : call.Room.Realm is not null
                        ? $"{call.Room.Name ?? "Unknown"}, {call.Room.Realm.Name}"
                    : call.Room.Name ?? "Unknown",
                IsAdmin = isAdmin,
                Participants = participants,
            }
        );
    }

    [HttpPost("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> StartCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var now = SystemClock.Instance.GetCurrentInstant();

        var member = await GetJoinedMemberAsync(roomId, accountId);
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member to start a call.", forbidden: true));
        if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            return StatusCode(403, ApiError.Unauthorized("You has been timed out in this chat.", forbidden: true));

        // Backward compatible: this endpoint now only ensures the long-lived call record/session exists.
        var call = await EnsureRealtimeCallAsync(roomId, member);
        return Ok(call);
    }

    [HttpPost("{roomId:guid}/invite/{targetAccountId:guid}")]
    [AskPermission(PermissionKeys.ChatCallInvite)]
    [Authorize]
    public async Task<IActionResult> InviteToCall(Guid roomId, Guid targetAccountId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var now = SystemClock.Instance.GetCurrentInstant();

        var member = await GetJoinedMemberAsync(roomId, accountId, includeRoom: true);
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member to invite someone.", forbidden: true));
        if (member.TimeoutUntil.HasValue && member.TimeoutUntil.Value > now)
            return StatusCode(403, ApiError.Unauthorized("You have been timed out in this chat.", forbidden: true));

        // Target must also be a member of the room
        var targetMember = await GetJoinedMemberAsync(roomId, targetAccountId, includeRoom: true);
        if (targetMember is null)
            return StatusCode(403, ApiError.Unauthorized("Target user is not a member of this room.", forbidden: true));

        var call = await EnsureRealtimeCallAsync(roomId, member);
        if (string.IsNullOrWhiteSpace(call.SessionId))
            return BadRequest(new ApiError { Code = "CHAT_CALL_SESSION_NOT_CONFIGURED", Message = "Call session is not properly configured.", Status = 400 });

        // Fire-and-forget VoIP push via Ring gRPC
        try
        {
            var callerName = member.Nick ?? member.Account?.Nick ?? "Someone";
            var callerPfp = currentUser.Picture?.Id;
            var account = await accounts.GetAccountAsync(
                new DyGetAccountRequest { Id = targetAccountId.ToString() }
            );
            var locale = account.Language;
            var roomSubject =
                call.Room is { Type: ChatRoomType.DirectMessage, Name: null } ? "DM"
                : call.Room.Realm is not null
                    ? $"{call.Room.Name ?? "Unknown"}, {call.Room.Realm.Name}"
                    : call.Room.Name ?? "Unknown";
            var invitePayload = new Dictionary<string, object>
            {
                ["uuid"] = Guid.NewGuid(),
                ["caller_id"] = accountId,
                ["caller_name"] = callerName,
                ["room_id"] = roomId,
                ["call_id"] = call.Id,
                ["room_name"] = roomSubject,
                ["session_id"] = call.SessionId,
            };
            if (!string.IsNullOrWhiteSpace(callerPfp))
                invitePayload["pfp"] = callerPfp;

            var ringClient =
                HttpContext.RequestServices.GetRequiredService<DyRingService.DyRingServiceClient>();
            var notification = new DyPushNotification
            {
                Topic = "call.incoming",
                Title = localization.Get(
                    "chatCallInviteTitle",
                    locale,
                    new { senderName = callerName, roomName = roomSubject }
                ),
                Body = localization.Get(
                    "chatCallInviteBody",
                    locale,
                    new { senderName = callerName }
                ),
                PushType = "VoIP",
                IsSavable = false,
                Meta = InfraObjectCoder.ConvertObjectToByteString(invitePayload),
            };

            var request = new DySendPushNotificationToUserRequest
            {
                UserId = targetAccountId.ToString(),
                Notification = notification,
            };

            _ = ringClient.SendPushNotificationToUserAsync(request);

            var ws = HttpContext.RequestServices.GetRequiredService<RemoteWebSocketService>();
            _ = ws.PushWebSocketPacket(
                targetAccountId.ToString(),
                WebSocketPacketType.CallInvited,
                InfraObjectCoder.ConvertObjectToByteString(invitePayload).ToByteArray()
            );
        }
        catch (Exception)
        {
            // ponytail: VoIP push is best-effort, don't fail the endpoint
        }

        return NoContent();
    }

    [HttpDelete("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult> LeaveCall(Guid roomId, [FromQuery(Name = "tool")] string? tool = null)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await GetJoinedMemberAsync(roomId, accountId, includeRoom: true);
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member to leave a call.", forbidden: true));

        var call = await EnsureRealtimeCallAsync(roomId, member);
        if (string.IsNullOrWhiteSpace(call.SessionId))
            return NoContent();

        var defaultIdentity = realtime.GetParticipantIdentity(currentUser);
        var participantIdentity = realtime.GetParticipantIdentity(currentUser, tool);
        var isTool = participantIdentity != defaultIdentity;

        var wasInCall = false;
        if (realtime is LiveKitRealtimeService livekitService)
        {
            var participants = await livekitService.SyncParticipantsAsync(call.SessionId);
            var currentParticipant = participants.FirstOrDefault(p => p.Identity == participantIdentity);
            if (currentParticipant != null)
            {
                await livekitService.KickParticipantAsync(
                    call.SessionId,
                    currentParticipant.Identity
                );
                wasInCall = true;
            }
        }

        if (wasInCall && !isTool)
        {
            var memberName = member.Nick ?? member.Account?.Nick ?? "Someone";
            await cs.SendSystemMessageAsync(
                call.Room,
                member,
                "system.call.member.left",
                $"{memberName} left the call.",
                new Dictionary<string, object>
                {
                    ["event"] = "call_member_left",
                    ["reason"] = "left",
                    ["call_id"] = call.Id,
                    ["member_id"] = member.Id,
                    ["account_id"] = member.AccountId,
                }
            );
        }

        return NoContent();
    }

    private async Task<IActionResult> ToggleMuteParticipant(
        Guid roomId,
        Guid targetAccountId,
        bool mute
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await GetJoinedMemberAsync(roomId, accountId, includeRoom: true);
        if (member is null)
            return StatusCode(403, ApiError.Unauthorized("You need to be a member.", forbidden: true));

        var call = await EnsureRealtimeCallAsync(roomId, member);
        if (string.IsNullOrWhiteSpace(call.SessionId))
            return BadRequest(new ApiError { Code = "CHAT_CALL_SESSION_NOT_CONFIGURED", Message = "Call session is not properly configured.", Status = 400 });

        var roomOwnerId = call.Room.AccountId ?? member.ChatRoom.AccountId;
        var isAdmin =
            member.AccountId == roomOwnerId || call.Room.Type == ChatRoomType.DirectMessage;
        if (!isAdmin)
            return StatusCode(403, ApiError.Unauthorized("Only room admin can mute participants.", forbidden: true));

        if (realtime is LiveKitRealtimeService livekitService)
        {
            var participants = await livekitService.SyncParticipantsAsync(call.SessionId);
            var targetParticipant = participants.FirstOrDefault(p =>
                p.AccountId == targetAccountId
            );

            if (targetParticipant == null)
                return NotFound(ApiError.NotFound("participant", message: "Target participant not found in call.", code: "CHAT_CALL_PARTICIPANT_NOT_FOUND"));

            if (string.IsNullOrEmpty(targetParticipant.TrackSid))
                return BadRequest(new ApiError { Code = "CHAT_CALL_NO_TRACK", Message = "No track available to mute.", Status = 400 });

            await livekitService.MuteParticipantAsync(
                call.SessionId,
                targetParticipant.Identity,
                targetParticipant.TrackSid,
                mute
            );
        }

        return NoContent();
    }

    private async Task<SnChatMember?> GetJoinedMemberAsync(
        Guid roomId,
        Guid accountId,
        bool includeRoom = false
    )
    {
        var query = db.ChatMembers.Where(m =>
            m.AccountId == accountId
            && m.ChatRoomId == roomId
            && m.JoinedAt != null
            && m.LeaveAt == null
        );

        if (includeRoom)
            query = query.Include(m => m.ChatRoom);

        var member = await query.FirstOrDefaultAsync();
        if (member is null)
            return null;

        if (member.Account is null)
            member = await crs.LoadMemberAccount(member);

        return member;
    }

    private async Task<SnRealtimeCall> EnsureRealtimeCallAsync(Guid roomId, SnChatMember member)
    {
        var call = await db
            .ChatRealtimeCall.Where(c => c.RoomId == roomId)
            .Where(c => c.EndedAt == null)
            .Include(c => c.Room)
            .Include(c => c.Sender)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();

        if (call is null)
        {
            var room = member.ChatRoom;
            if (room is null)
            {
                room = await db.ChatRooms.Where(r => r.Id == roomId).FirstAsync();
            }

            call = new SnRealtimeCall
            {
                RoomId = roomId,
                SenderId = member.Id,
                Room = room,
                Sender = member,
                ProviderName = realtime.ProviderName,
            };

            db.ChatRealtimeCall.Add(call);
            await db.SaveChangesAsync();
        }

        var sessionConfig = await realtime.CreateSessionAsync(
            roomId,
            new Dictionary<string, object>
            {
                ["room_id"] = roomId,
                ["call_id"] = call.Id,
                ["user_id"] = member.AccountId,
            }
        );

        var updated = false;

        if (call.ProviderName != realtime.ProviderName)
        {
            call.ProviderName = realtime.ProviderName;
            updated = true;
        }

        if (
            !string.IsNullOrWhiteSpace(sessionConfig.SessionId)
            && call.SessionId != sessionConfig.SessionId
        )
        {
            call.SessionId = sessionConfig.SessionId;
            updated = true;
        }

        if (sessionConfig.Parameters.Count > 0)
        {
            call.UpstreamConfig = sessionConfig.Parameters;
            updated = true;
        }

        if (updated)
        {
            db.ChatRealtimeCall.Update(call);
            await db.SaveChangesAsync();
        }

        if (call.Room is null)
            call.Room =
                member.ChatRoom ?? await db.ChatRooms.Where(r => r.Id == roomId).FirstAsync();
        if (call.Sender is null)
            call.Sender = member;

        return call;
    }

    private async Task<List<ParticipantCacheItem>> SyncProviderParticipantsAsync(string sessionId)
    {
        if (realtime is LiveKitRealtimeService livekitService)
            return await livekitService.SyncParticipantsAsync(sessionId);

        return [];
    }

    private async Task<List<CallParticipant>> BuildParticipantsAsync(
        Guid roomId,
        List<ParticipantCacheItem> roomParticipants
    )
    {
        var chatRoomService = HttpContext.RequestServices.GetRequiredService<ChatRoomService>();
        var participants = new List<CallParticipant>(roomParticipants.Count);

        foreach (var p in roomParticipants)
        {
            var participant = new CallParticipant
            {
                Identity = p.Identity,
                Name = p.Name,
                AccountId = p.AccountId,
                JoinedAt = p.JoinedAt,
                TrackSid = p.TrackSid,
            };

            if (p.AccountId.HasValue)
                participant.Profile = await chatRoomService.GetRoomMember(
                    p.AccountId.Value,
                    roomId
                );

            participants.Add(participant);
        }

        return participants;
    }
}

// Response model for joining a call
public class JoinCallResponse
{
    /// <summary>
    /// The service provider name (e.g., "LiveKit")
    /// </summary>
    public string Provider { get; set; } = null!;

    /// <summary>
    /// The LiveKit server endpoint
    /// </summary>
    public string Endpoint { get; set; } = null!;

    /// <summary>
    /// Authentication token for the user
    /// </summary>
    public string Token { get; set; } = null!;

    /// <summary>
    /// The call identifier
    /// </summary>
    public Guid CallId { get; set; }

    /// <summary>
    /// The room name in LiveKit
    /// </summary>
    public string RoomName { get; set; } = null!;

    /// <summary>
    /// Human-readable room title (e.g. "General", "DM")
    /// </summary>
    public string? RoomTitle { get; set; }

    /// <summary>
    /// Whether the user is the admin of the call
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Current participants in the call
    /// </summary>
    public List<CallParticipant> Participants { get; set; } = new();
}

/// <summary>
/// Represents a participant in a real-time call
/// </summary>
public class CallParticipant
{
    /// <summary>
    /// The participant's identity (username)
    /// </summary>
    public string Identity { get; set; } = null!;

    /// <summary>
    /// The participant's display name
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// The participant's account ID if available
    /// </summary>
    public Guid? AccountId { get; set; }

    /// <summary>
    /// The participant's profile in the chat
    /// </summary>
    public SnChatMember? Profile { get; set; }

    /// <summary>
    /// When the participant joined the call
    /// </summary>
    public DateTime JoinedAt { get; set; }

    /// <summary>
    /// The participant's track SID (for muting)
    /// </summary>
    public string? TrackSid { get; set; }
}

public class KickParticipantRequest
{
    public int? BanDurationMinutes { get; set; }
    public string? Reason { get; set; }
}
