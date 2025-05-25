using DysonNetwork.Sphere.Connection;
using DysonNetwork.Sphere.Storage;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text.Json;

namespace DysonNetwork.Sphere.Chat.Realtime;

/// <summary>
/// LiveKit implementation of the real-time communication service
/// </summary>
public class LivekitRealtimeService : IRealtimeService
{
    private readonly AppDatabase _db;
    private readonly ICacheService _cache;
    private readonly WebSocketService _ws;

    private readonly ILogger<LivekitRealtimeService> _logger;
    private readonly RoomServiceClient _roomService;
    private readonly AccessToken _accessToken;
    private readonly WebhookReceiver _webhookReceiver;

    public LivekitRealtimeService(
        IConfiguration configuration,
        ILogger<LivekitRealtimeService> logger,
        AppDatabase db,
        ICacheService cache,
        WebSocketService ws
    )
    {
        _logger = logger;

        // Get LiveKit configuration from appsettings
        var host = configuration["RealtimeChat:Endpoint"] ??
                   throw new ArgumentNullException("Endpoint configuration is required");
        var apiKey = configuration["RealtimeChat:ApiKey"] ??
                     throw new ArgumentNullException("ApiKey configuration is required");
        var apiSecret = configuration["RealtimeChat:ApiSecret"] ??
                        throw new ArgumentNullException("ApiSecret configuration is required");

        _roomService = new RoomServiceClient(host, apiKey, apiSecret);
        _accessToken = new AccessToken(apiKey, apiSecret);
        _webhookReceiver = new WebhookReceiver(apiKey, apiSecret);

        _db = db;
        _cache = cache;
        _ws = ws;
    }

    /// <inheritdoc />
    public string ProviderName => "LiveKit";

    /// <inheritdoc />
    public async Task<RealtimeSessionConfig> CreateSessionAsync(Guid roomId, Dictionary<string, object> metadata)
    {
        try
        {
            var roomName = $"Call_{roomId.ToString().Replace("-", "")}";

            // Convert metadata to a string dictionary for LiveKit
            var roomMetadata = new Dictionary<string, string>();
            foreach (var item in metadata)
            {
                roomMetadata[item.Key] = item.Value?.ToString() ?? string.Empty;
            }

            // Create room in LiveKit
            var room = await _roomService.CreateRoom(new CreateRoomRequest
            {
                Name = roomName,
                EmptyTimeout = 300, // 5 minutes
                Metadata = JsonSerializer.Serialize(roomMetadata)
            });

            // Return session config
            return new RealtimeSessionConfig
            {
                SessionId = room.Name,
                Parameters = new Dictionary<string, object>
                {
                    { "sid", room.Sid },
                    { "emptyTimeout", room.EmptyTimeout },
                    { "creationTime", room.CreationTime }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create LiveKit room for roomId: {RoomId}", roomId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(string sessionId, RealtimeSessionConfig config)
    {
        try
        {
            // Delete the room in LiveKit
            await _roomService.DeleteRoom(new DeleteRoomRequest
            {
                Room = sessionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end LiveKit session: {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public string GetUserToken(Account.Account account, string sessionId, bool isAdmin = false)
    {
        var token = _accessToken.WithIdentity(account.Name)
            .WithName(account.Nick)
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                CanPublish = true,
                CanPublishData = true,
                CanSubscribe = true,
                CanSubscribeMetrics = true,
                RoomAdmin = isAdmin,
                Room = sessionId
            })
            .WithAttributes(new Dictionary<string, string> { { "account_id", account.Id.ToString() } })
            .WithTtl(TimeSpan.FromHours(1));
        return token.ToJwt();
    }

    public async Task ReceiveWebhook(string body, string authHeader)
    {
        var evt = _webhookReceiver.Receive(body, authHeader);
        if (evt is null) return;

        switch (evt.Event)
        {
            case "room_finished":
                var now = SystemClock.Instance.GetCurrentInstant();
                await _db.ChatRealtimeCall
                    .Where(c => c.SessionId == evt.Room.Name)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.EndedAt, now)
                    );

                // Also clean up participants list when the room is finished
                await _cache.RemoveAsync(_GetParticipantsKey(evt.Room.Name));
                break;

            case "participant_joined":
                if (evt.Participant != null)
                {
                    // Add the participant to cache
                    await _AddParticipantToCache(evt.Room.Name, evt.Participant);
                    _logger.LogInformation(
                        "Participant joined room: {RoomName}, Participant: {ParticipantIdentity}",
                        evt.Room.Name, evt.Participant.Identity);

                    // Broadcast participant list update to all participants
                    await _BroadcastParticipantUpdate(evt.Room.Name);
                }
                break;

            case "participant_left":
                if (evt.Participant != null)
                {
                    // Remove the participant from cache
                    await _RemoveParticipantFromCache(evt.Room.Name, evt.Participant);
                    _logger.LogInformation(
                        "Participant left room: {RoomName}, Participant: {ParticipantIdentity}",
                        evt.Room.Name, evt.Participant.Identity);

                    // Broadcast participant list update to all participants
                    await _BroadcastParticipantUpdate(evt.Room.Name);
                }
                break;
        }
    }

    private static string _GetParticipantsKey(string roomName)
        => $"RoomParticipants_{roomName}";

    private async Task _AddParticipantToCache(string roomName, ParticipantInfo participant)
    {
        var participantsKey = _GetParticipantsKey(roomName);

        // Try to acquire a lock to prevent race conditions when updating the participants list
        await using var lockObj = await _cache.AcquireLockAsync(
            $"{participantsKey}_lock",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5));

        if (lockObj == null)
        {
            _logger.LogWarning("Failed to acquire lock for updating participants list in room: {RoomName}", roomName);
            return;
        }

        // Get the current participants list
        var participants = await _cache.GetAsync<List<ParticipantCacheItem>>(participantsKey) ??
                           new List<ParticipantCacheItem>();

        // Check if the participant already exists
        var existingIndex = participants.FindIndex(p => p.Identity == participant.Identity);
        if (existingIndex >= 0)
        {
            // Update existing participant
            participants[existingIndex] = CreateParticipantCacheItem(participant);
        }
        else
        {
            // Add new participant
            participants.Add(CreateParticipantCacheItem(participant));
        }

        // Update cache with new list
        await _cache.SetAsync(participantsKey, participants, TimeSpan.FromHours(6));

        // Also add to a room group in cache for easy cleanup
        await _cache.AddToGroupAsync(participantsKey, $"Room_{roomName}");
    }

    private async Task _RemoveParticipantFromCache(string roomName, ParticipantInfo participant)
    {
        var participantsKey = _GetParticipantsKey(roomName);

        // Try to acquire a lock to prevent race conditions when updating the participants list
        await using var lockObj = await _cache.AcquireLockAsync(
            $"{participantsKey}_lock",
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(5));

        if (lockObj == null)
        {
            _logger.LogWarning("Failed to acquire lock for updating participants list in room: {RoomName}", roomName);
            return;
        }

        // Get current participants list
        var participants = await _cache.GetAsync<List<ParticipantCacheItem>>(participantsKey);
        if (participants == null || !participants.Any())
            return;

        // Remove participant
        participants.RemoveAll(p => p.Identity == participant.Identity);

        // Update cache with new list
        await _cache.SetAsync(participantsKey, participants, TimeSpan.FromHours(6));
    }

    // Helper method to get participants in a room
    public async Task<List<ParticipantCacheItem>> GetRoomParticipantsAsync(string roomName)
    {
        var participantsKey = _GetParticipantsKey(roomName);
        return await _cache.GetAsync<List<ParticipantCacheItem>>(participantsKey) ?? new List<ParticipantCacheItem>();
    }

    // Class to represent a participant in the cache
    public class ParticipantCacheItem
    {
        public string Identity { get; set; } = null!;
        public string Name { get; set; } = null!;
        public Guid? AccountId { get; set; }
        public ParticipantInfo.Types.State State { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
        public DateTime JoinedAt { get; set; }
    }

    private ParticipantCacheItem CreateParticipantCacheItem(ParticipantInfo participant)
    {
        // Try to parse account ID from metadata
        Guid? accountId = null;
        var metadata = new Dictionary<string, string>();
        
        if (!string.IsNullOrEmpty(participant.Metadata))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(participant.Metadata) ?? 
                           new Dictionary<string, string>();
                
                if (metadata.TryGetValue("account_id", out var accountIdStr))
                {
                    if (Guid.TryParse(accountIdStr, out var parsedId))
                    {
                        accountId = parsedId;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse participant metadata");
            }
        }

        return new ParticipantCacheItem
        {
            Identity = participant.Identity,
            Name = participant.Name,
            AccountId = accountId,
            State = participant.State,
            Metadata = metadata,
            JoinedAt = DateTime.UtcNow
        };
    }
    
    // Broadcast participant update to all participants in a room
    private async Task _BroadcastParticipantUpdate(string roomName)
    {
        try
        {
            // Get the room ID from the session name
            var roomInfo = await _db.ChatRealtimeCall
                .Where(c => c.SessionId == roomName && c.EndedAt == null)
                .Select(c => new { c.RoomId, c.Id })
                .FirstOrDefaultAsync();
        
            if (roomInfo == null)
            {
                _logger.LogWarning("Could not find room info for session: {SessionName}", roomName);
                return;
            }
        
            // Get current participants
            var livekitParticipants = await GetRoomParticipantsAsync(roomName);
        
            // Get all room members who should receive this update
            var roomMembers = await _db.ChatMembers
                .Where(m => m.ChatRoomId == roomInfo.RoomId && m.LeaveAt == null)
                .Select(m => m.AccountId)
                .ToListAsync();
        
            // Get member profiles for participants who have account IDs
            var accountIds = livekitParticipants
                .Where(p => p.AccountId.HasValue)
                .Select(p => p.AccountId!.Value)
                .ToList();
        
            var memberProfiles = new Dictionary<Guid, ChatMember>();
            if (accountIds.Any())
            {
                memberProfiles = await _db.ChatMembers
                    .Where(m => m.ChatRoomId == roomInfo.RoomId && accountIds.Contains(m.AccountId))
                    .ToDictionaryAsync(m => m.AccountId, m => m);
            }
        
            // Convert to CallParticipant objects
            var participants = livekitParticipants.Select(p => new CallParticipant
            {
                Identity = p.Identity,
                Name = p.Name,
                AccountId = p.AccountId,
                JoinedAt = p.JoinedAt,
                Profile = p.AccountId.HasValue && memberProfiles.TryGetValue(p.AccountId.Value, out var profile) 
                    ? profile 
                    : null
            }).ToList();
        
            // Create the update packet with CallParticipant objects
            var updatePacket = new WebSocketPacket
            {
                Type = WebSocketPacketType.CallParticipantsUpdate,
                Data = new Dictionary<string, object>
                {
                    { "room_id", roomInfo.RoomId },
                    { "call_id", roomInfo.Id },
                    { "participants", participants }
                }
            };
        
            // Send the update to all members
            foreach (var accountId in roomMembers)
            {
                _ws.SendPacketToAccount(accountId, updatePacket);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting participant update for room {RoomName}", roomName);
        }
    }
}