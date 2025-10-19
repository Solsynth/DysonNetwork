using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using DysonNetwork.Shared.Proto;
using WebSocketPacket = DysonNetwork.Shared.Models.WebSocketPacket;

namespace DysonNetwork.Sphere.Chat;

public class RealtimeChatConfiguration
{
    public string Endpoint { get; set; } = null!;
}

public class SignalingMessage
{
    public string Type { get; set; } = null!;
    public object? Data { get; set; }
    public string? AccountId { get; set; }
    public SnAccount? Account { get; set; }
}

[ApiController]
[Route("/api/chat/realtime")]
public class RealtimeCallController(
    IConfiguration configuration,
    AppDatabase db,
    ChatService cs,
    ChatRoomService crs,
    ILogger<RealtimeCallController> logger
) : ControllerBase
{
    private readonly RealtimeChatConfiguration _config =
        configuration.GetSection("RealtimeChat").Get<RealtimeChatConfiguration>()!;

    // A thread-safe collection to hold connected WebSocket clients per chat room.
    private static readonly
        ConcurrentDictionary<string, ConcurrentDictionary<Guid, (WebSocket Socket, string
            AccountId, int Role)>> RoomClients = new();

    // A thread-safe collection to hold participants in each room.
    private static readonly
        ConcurrentDictionary<string, ConcurrentDictionary<string, (Account Account, DateTime JoinedAt)>>
        RoomParticipants = new();

    /// <summary>
    /// This endpoint is for WebRTC signaling webhooks if needed in the future.
    /// Currently built-in WebRTC signaling doesn't require external webhooks.
    /// </summary>
    [HttpPost("webhook")]
    [SwaggerIgnore]
    public Task<IActionResult> WebhookReceiver()
    {
        // Built-in WebRTC signaling doesn't require webhooks
        // Return success to indicate endpoint exists for potential future use
        return Task.FromResult<IActionResult>(Ok("Webhook received - built-in WebRTC signaling active"));
    }

    [HttpGet("{roomId:guid}/status")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> GetOngoingCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a member to view call status.");

        var ongoingCall = await db.ChatRealtimeCall
            .Where(c => c.RoomId == roomId)
            .Where(c => c.EndedAt == null)
            .Include(c => c.Room)
            .Include(c => c.Sender)
            .FirstOrDefaultAsync();
        if (ongoingCall is null) return NotFound();
        ongoingCall.Sender = await crs.LoadMemberAccount(ongoingCall.Sender);
        return Ok(ongoingCall);
    }

    [HttpGet("{roomId:guid}/join")]
    [Authorize]
    public async Task<ActionResult<JoinCallResponse>> JoinCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        // Check if the user is a member of the chat room
        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a member to join a call.");

        // Get ongoing call
        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is null)
            return NotFound("There is no ongoing call in this room.");

        // Check if session ID exists
        if (string.IsNullOrEmpty(ongoingCall.SessionId))
            return BadRequest("Call session is not properly configured.");

        var isAdmin = member.Role >= ChatMemberRole.Moderator;

        // Get WebRTC signaling server endpoint from configuration
        var endpoint = _config.Endpoint ??
                       throw new InvalidOperationException("WebRTC signaling endpoint configuration is missing");

        // Get current participants from the participant list
        var participants = new List<CallParticipant>();
        var roomKey = ongoingCall.RoomId.ToString();
        if (RoomParticipants.TryGetValue(roomKey, out var partsDict))
        {
            participants.AddRange(from part in partsDict.Values
                select new CallParticipant
                {
                    Identity = part.Account.Id,
                    Name = part.Account.Name,
                    AccountId = Guid.Parse(part.Account.Id),
                    JoinedAt = part.JoinedAt
                });
        }

        // Create the response model for built-in WebRTC signaling
        var response = new JoinCallResponse
        {
            Provider = "Built-in WebRTC Signaling",
            Endpoint = endpoint,
            Token = "", // No external token needed for built-in signaling
            CallId = ongoingCall.Id,
            RoomName = ongoingCall.SessionId,
            IsAdmin = isAdmin,
            Participants = participants
        };

        return Ok(response);
    }

    [HttpPost("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> StartCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .Include(m => m.ChatRoom)
            .FirstOrDefaultAsync();
        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a normal member to start a call.");

        var ongoingCall = await cs.GetCallOngoingAsync(roomId);
        if (ongoingCall is not null) return StatusCode(423, "There is already an ongoing call inside the chatroom.");
        var call = await cs.CreateCallAsync(member.ChatRoom, member);
        return Ok(call);
    }

    [HttpDelete("{roomId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealtimeCall>> EndCall(Guid roomId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == roomId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member == null || member.Role < ChatMemberRole.Member)
            return StatusCode(403, "You need to be a normal member to end a call.");

        try
        {
            await cs.EndCallAsync(roomId, member);
            return NoContent();
        }
        catch (Exception exception)
        {
            return BadRequest(exception.Message);
        }
    }

    /// <summary>
    /// WebSocket signaling endpoint for WebRTC calls in a specific chat room.
    /// Path: /api/chat/realtime/{chatId}
    /// Requires JWT authentication (handled by middleware).
    /// </summary>
    [HttpGet("{chatId:guid}")]
    public async Task SignalingWebSocket(Guid chatId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
        {
            HttpContext.Response.StatusCode = 401;
            await HttpContext.Response.WriteAsync("Unauthorized");
            return;
        }

        // Verify the user is a member of the chat room
        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.ChatMembers
            .Where(m => m.AccountId == accountId && m.ChatRoomId == chatId && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member == null || member.Role < ChatMemberRole.Member)
        {
            HttpContext.Response.StatusCode = 403;
            await HttpContext.Response.WriteAsync("Forbidden: Not a member of this chat room");
            return;
        }

        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = 400;
            await HttpContext.Response.WriteAsync("Bad Request: WebSocket connection expected");
            return;
        }

        var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var clientId = Guid.NewGuid();

        // Add a client to the room-specific clients dictionary
        var roomKey = chatId.ToString();
        var roomDict = RoomClients.GetOrAdd(roomKey,
            _ => new ConcurrentDictionary<Guid, (WebSocket, string, int)>());
        roomDict.TryAdd(clientId, (webSocket, currentUser.Id, member.Role));

        // Add to the participant list
        var participantsDict = RoomParticipants.GetOrAdd(roomKey,
            _ => new ConcurrentDictionary<string, (Account Account, DateTime JoinedAt)>());
        var wasAdded = participantsDict.TryAdd(currentUser.Id, (currentUser, DateTime.UtcNow));

        logger.LogInformation(
            "WebRTC signaling client connected: {ClientId} ({UserId}) in room {RoomId}. Total clients in room: {Count}",
            clientId, currentUser.Id, chatId, roomDict.Count);

        // Get other participants as CallParticipant objects
        var otherParticipants = participantsDict.Values
            .Where(p => p.Account.Id != currentUser.Id)
            .Select(p => new CallParticipant
            {
                Identity = p.Account.Id,
                Name = p.Account.Name,
                AccountId = Guid.Parse(p.Account.Id),
                Account = SnAccount.FromProtoValue(p.Account),
                JoinedAt = p.JoinedAt
            })
            .ToList();

        var welcomePacket = new WebSocketPacket
        {
            Type = "webrtc",
            Data = new
            {
                userId = currentUser.Id,
                roomId = chatId,
                message = $"Connected to call of #{chatId}.",
                timestamp = DateTime.UtcNow.ToString("o"),
                participants = otherParticipants
            }
        };
        var responseBytes = welcomePacket.ToBytes();
        await webSocket.SendAsync(new ArraySegment<byte>(responseBytes), WebSocketMessageType.Text, true,
            CancellationToken.None);

        // Broadcast user-joined to existing clients if this is the first connection for this user in the room
        if (wasAdded)
        {
            var joinPacket = new WebSocketPacket
            {
                Type = "webrtc.signal",
                Data = new SignalingMessage
                {
                    Type = "user-joined",
                    AccountId = currentUser.Id,
                    Account = SnAccount.FromProtoValue(currentUser),
                    Data = new { }
                }
            };
            await BroadcastMessageToRoom(chatId, clientId, joinPacket);
        }

        try
        {
            // Use a MemoryStream to build the full message from potentially multiple chunks.
            using var ms = new MemoryStream();
            // A larger buffer can be more efficient, but the loop is what handles correctness.
            var buffer = new byte[1024 * 8];

            while (webSocket.State == WebSocketState.Open)
            {
                ms.SetLength(0); // Clear the stream for the new message.
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                var packet = WebSocketPacket.FromBytes(ms.ToArray());
                var signalingMessage = packet.GetData<SignalingMessage>();
                if (signalingMessage is null)
                {
                    logger.LogWarning("Signaling message could not be parsed, dismissed...");
                    continue;
                }
                
                signalingMessage.AccountId = currentUser.Id;
                signalingMessage.Account = SnAccount.FromProtoValue(currentUser);
                var broadcastPacket = new WebSocketPacket
                {
                    Type = "webrtc.signal",
                    Data = signalingMessage
                };
                
                logger.LogDebug("Message received from {ClientId} ({UserId}): Type={MessageType}", clientId, currentUser.Id, signalingMessage.Type);
                await BroadcastMessageToRoom(chatId, clientId, broadcastPacket);
            }
        }
        catch (WebSocketException wsex) when (wsex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            // This is an expected exception when a client closes the browser tab.
            logger.LogDebug("WebRTC signaling client connection was closed prematurely for user {UserId}",
                currentUser.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error with WebRTC signaling client connection for user {UserId}", currentUser.Id);
        }
        finally
        {
            // Remove the client from the room
            if (roomDict.TryRemove(clientId, out _))
            {
                logger.LogInformation(
                    "WebRTC signaling client disconnected: {ClientId} ({UserId}). Total clients in room: {Count}",
                    clientId, currentUser.Id, roomDict.Count);

                // If no more connections from this account, remove from participants
                if (roomDict.Values.All(v => v.AccountId != currentUser.Id))
                {
                    var tempParticipantsDict = RoomParticipants.GetOrAdd(roomKey,
                        _ => new ConcurrentDictionary<string, (Account Account, DateTime JoinedAt)>());
                    if (tempParticipantsDict.TryRemove(currentUser.Id, out _))
                    {
                        logger.LogInformation("Participant {UserId} removed from room {RoomId}", currentUser.Id,
                            chatId);
                    }
                }
            }

            webSocket.Dispose();
        }
    }

    private async Task BroadcastMessageToRoom(Guid roomId, Guid senderId, WebSocketPacket packet)
    {
        var roomKey = roomId.ToString();
        if (!RoomClients.TryGetValue(roomKey, out var roomDict))
            return;

        var messageBytes = packet.ToBytes();
        var segment = new ArraySegment<byte>(messageBytes);

        foreach (var pair in roomDict)
        {
            if (pair.Key == senderId) continue;

            if (pair.Value.Socket.State != WebSocketState.Open) continue;
            await pair.Value.Socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            logger.LogDebug("Message broadcasted to {ClientId} in room {RoomId}", pair.Key, roomId);
        }
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
    public SnAccount? Account { get; set; }

    /// <summary>
    /// When the participant joined the call
    /// </summary>
    public DateTime JoinedAt { get; set; }
}
