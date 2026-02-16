using Livekit.Server.Sdk.Dotnet;
using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Messager.Chat.Realtime;

public class LiveKitRealtimeService : IRealtimeService
{
    private readonly AppDatabase _db;
    private readonly ICacheService _cache;

    private readonly ILogger<LiveKitRealtimeService> _logger;
    private readonly RoomServiceClient _roomService;
    private readonly AccessToken _accessToken;

    public LiveKitRealtimeService(
        IConfiguration configuration,
        ILogger<LiveKitRealtimeService> logger,
        AppDatabase db,
        ICacheService cache
    )
    {
        _logger = logger;

        var host = configuration["RealtimeChat:Endpoint"] ??
                   throw new ArgumentNullException("Endpoint configuration is required");
        var apiKey = configuration["RealtimeChat:ApiKey"] ??
                     throw new ArgumentNullException("ApiKey configuration is required");
        var apiSecret = configuration["RealtimeChat:ApiSecret"] ??
                        throw new ArgumentNullException("ApiSecret configuration is required");

        _roomService = new RoomServiceClient(host, apiKey, apiSecret);
        _accessToken = new AccessToken(apiKey, apiSecret);

        _db = db;
        _cache = cache;
    }

    public string ProviderName => "LiveKit";

    public async Task<RealtimeSessionConfig> CreateSessionAsync(Guid roomId, Dictionary<string, object> metadata)
    {
        try
        {
            var roomName = $"Call_{roomId.ToString().Replace("-", "")}";

            var roomMetadata = new Dictionary<string, string>();
            foreach (var item in metadata)
            {
                roomMetadata[item.Key] = item.Value?.ToString() ?? string.Empty;
            }

            var room = await _roomService.CreateRoom(new CreateRoomRequest
            {
                Name = roomName,
                EmptyTimeout = 300,
                Metadata = JsonSerializer.Serialize(roomMetadata)
            });

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

    public async Task EndSessionAsync(string sessionId, RealtimeSessionConfig config)
    {
        try
        {
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

    public string GetUserToken(Account account, string sessionId, bool isAdmin = false)
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
            .WithMetadata(JsonSerializer.Serialize(new Dictionary<string, string>
                { { "account_id", account.Id.ToString() } }))
            .WithTtl(TimeSpan.FromHours(1));
        return token.ToJwt();
    }

    public async Task KickParticipantAsync(string sessionId, string identity)
    {
        try
        {
            await _roomService.RemoveParticipant(new RoomParticipantIdentity
            {
                Room = sessionId,
                Identity = identity
            });
            _logger.LogInformation("Kicked participant {Identity} from room {SessionId}", identity, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kick participant {Identity} from room {SessionId}", identity, sessionId);
            throw;
        }
    }

    public async Task MuteParticipantAsync(string sessionId, string identity, string trackSid, bool muted)
    {
        try
        {
            await _roomService.MutePublishedTrack(new MuteRoomTrackRequest
            {
                Room = sessionId,
                Identity = identity,
                TrackSid = trackSid,
                Muted = muted
            });
            _logger.LogInformation("Mute state changed for participant {Identity}: {Muted}, track {TrackSid}", identity, muted, trackSid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mute participant {Identity} in room {SessionId}", identity, sessionId);
            throw;
        }
    }

    public async Task<ParticipantInfo?> GetParticipantAsync(string sessionId, string identity)
    {
        try
        {
            var response = await _roomService.GetParticipant(new RoomParticipantIdentity
            {
                Room = sessionId,
                Identity = identity
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get participant {Identity} from room {SessionId}", identity, sessionId);
            return null;
        }
    }

    public async Task<List<ParticipantCacheItem>> SyncParticipantsAsync(string sessionId)
    {
        try
        {
            var response = await _roomService.ListParticipants(new ListParticipantsRequest
            {
                Room = sessionId
            });

            var participants = new List<ParticipantCacheItem>();
            foreach (var p in response.Participants)
            {
                participants.Add(CreateParticipantCacheItem(p));
            }

            var participantsKey = _GetParticipantsKey(sessionId);
            await using var lockObj = await _cache.AcquireLockAsync(
                $"{participantsKey}_lock",
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(5));

            if (lockObj == null)
            {
                _logger.LogWarning("Failed to acquire lock for syncing participants in room: {SessionId}", sessionId);
                return participants;
            }

            await _cache.SetAsync(participantsKey, participants, TimeSpan.FromHours(6));
            _logger.LogInformation("Synced {Count} participants for room {SessionId}", participants.Count, sessionId);

            return participants;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync participants for room {SessionId}", sessionId);
            throw;
        }
    }

    private static string _GetParticipantsKey(string roomName)
        => $"RoomParticipants_{roomName}";

    public async Task<List<ParticipantCacheItem>> GetRoomParticipantsAsync(string roomName)
    {
        var participantsKey = _GetParticipantsKey(roomName);
        return await _cache.GetAsync<List<ParticipantCacheItem>>(participantsKey) ?? [];
    }

    private ParticipantCacheItem CreateParticipantCacheItem(ParticipantInfo participant)
    {
        Guid? accountId = null;
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrEmpty(participant.Metadata))
        {
            try
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(participant.Metadata) ??
                           new Dictionary<string, string>();

                if (metadata.TryGetValue("account_id", out var accountIdStr))
                    if (Guid.TryParse(accountIdStr, out var parsedId))
                        accountId = parsedId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse participant metadata");
            }
        }

        var trackSid = participant.Tracks.FirstOrDefault()?.Sid;

        return new ParticipantCacheItem
        {
            Identity = participant.Identity,
            Name = participant.Name,
            AccountId = accountId,
            State = participant.State,
            Metadata = metadata,
            JoinedAt = DateTime.UtcNow,
            TrackSid = trackSid
        };
    }
}
