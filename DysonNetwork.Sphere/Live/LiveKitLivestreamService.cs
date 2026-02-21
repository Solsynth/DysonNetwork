using System.Text.Json;
using Livekit.Server.Sdk.Dotnet;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.Live;

public class LiveKitLivestreamService
{
    private readonly ILogger<LiveKitLivestreamService> _logger;
    private readonly RoomServiceClient _roomService;
    private readonly IngressServiceClient _ingressService;
    private readonly EgressServiceClient _egressService;
    private readonly AccessToken _accessToken;
    private readonly string _host;
    public string Host => _host;

    private readonly string? _playbackUrl;
    public string? PlaybackUrl => _playbackUrl;

    public LiveKitLivestreamService(IConfiguration configuration, ILogger<LiveKitLivestreamService> logger)
    {
        _logger = logger;

        var host = configuration["LiveStream:Endpoint"] ??
                   configuration["RealtimeChat:Endpoint"] ??
                   throw new ArgumentNullException("LiveStream:Endpoint configuration is required");
        var apiKey = configuration["LiveStream:ApiKey"] ??
                     configuration["RealtimeChat:ApiKey"] ??
                     throw new ArgumentNullException("LiveStream:ApiKey configuration is required");
        var apiSecret = configuration["LiveStream:ApiSecret"] ??
                        configuration["RealtimeChat:ApiSecret"] ??
                        throw new ArgumentNullException("LiveStream:ApiSecret configuration is required");

        _host = host;
        _playbackUrl = configuration["LiveStream:PlaybackUrl"];
        _roomService = new RoomServiceClient(host, apiKey, apiSecret);
        _ingressService = new IngressServiceClient(host, apiKey, apiSecret);
        _egressService = new EgressServiceClient(host, apiKey, apiSecret);
        _accessToken = new AccessToken(apiKey, apiSecret);
    }

    public string? BuildHlsPlaylistUrl(string? playlistPath)
    {
        if (string.IsNullOrEmpty(playlistPath))
            return null;

        if (string.IsNullOrEmpty(_playbackUrl))
            return null;

        return $"{_playbackUrl.TrimEnd('/')}/{playlistPath.TrimStart('/')}";
    }

    public async Task<LiveKitRoomResult> CreateRoomAsync(string roomName, Dictionary<string, string>? metadata = null)
    {
        try
        {
            var request = new CreateRoomRequest
            {
                Name = roomName,
                EmptyTimeout = 600,
                MaxParticipants = 10000,
            };

            if (metadata != null && metadata.Count > 0)
            {
                request.Metadata = JsonSerializer.Serialize(metadata);
            }

            var room = await _roomService.CreateRoom(request);
            _logger.LogInformation("Created LiveKit room: {RoomName}", roomName);

            return new LiveKitRoomResult
            {
                RoomName = room.Name,
                Sid = room.Sid,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create LiveKit room: {RoomName}", roomName);
            throw;
        }
    }

    public async Task DeleteRoomAsync(string roomName)
    {
        try
        {
            await _roomService.DeleteRoom(new DeleteRoomRequest
            {
                Room = roomName
            });
            _logger.LogInformation("Deleted LiveKit room: {RoomName}", roomName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete LiveKit room: {RoomName}", roomName);
            throw;
        }
    }

    public async Task<LiveKitIngressResult> CreateIngressAsync(string roomName,
        string participantIdentity,
        string? participantName = null,
        string? title = null,
        bool enableTranscoding = true,
        IngressInput inputType = IngressInput.RtmpInput)
    {
        try
        {
            var request = new CreateIngressRequest
            {
                InputType = inputType,
                Name = title ?? roomName,
                RoomName = roomName,
                ParticipantIdentity = participantIdentity,
                EnableTranscoding = enableTranscoding,
            };

            if (!string.IsNullOrEmpty(participantName))
            {
                request.ParticipantName = participantName;
            }

            var ingress = await _ingressService.CreateIngress(request);
            _logger.LogInformation("Created ingress for room: {RoomName}, ingressId: {IngressId}, inputType: {InputType}", 
                roomName, ingress.IngressId, inputType);

            return new LiveKitIngressResult
            {
                IngressId = ingress.IngressId,
                Url = ingress.Url,
                StreamKey = ingress.StreamKey,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ingress for room: {RoomName}", roomName);
            throw;
        }
    }

    public async Task<LiveKitIngressResult> CreateWhipIngressAsync(string roomName,
        string participantIdentity,
        string? participantName = null,
        string? title = null,
        bool enableTranscoding = false)
    {
        return await CreateIngressAsync(roomName, participantIdentity, participantName, title, enableTranscoding, IngressInput.WhipInput);
    }

    public async Task DeleteIngressAsync(string ingressId)
    {
        try
        {
            await _ingressService.DeleteIngress(new DeleteIngressRequest
            {
                IngressId = ingressId
            });
            _logger.LogInformation("Deleted ingress: {IngressId}", ingressId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete ingress: {IngressId}", ingressId);
            throw;
        }
    }

    public async Task<LiveKitEgressResult> StartRoomCompositeEgressAsync(
        string roomName,
        List<string>? rtmpUrls = null,
        string? filePath = null,
        string? layout = null)
    {
        try
        {
            var request = new RoomCompositeEgressRequest
            {
                RoomName = roomName,
                Layout = layout ?? "default",
            };

            if (rtmpUrls != null && rtmpUrls.Count > 0)
            {
                request.StreamOutputs.Add(new StreamOutput
                {
                    Protocol = StreamProtocol.Rtmp,
                    Urls = { rtmpUrls }
                });
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                request.FileOutputs.Add(new EncodedFileOutput
                {
                    Filepath = filePath,
                });
            }

            var egress = await _egressService.StartRoomCompositeEgress(request);
            _logger.LogInformation("Started egress for room: {RoomName}, egressId: {EgressId}", roomName,
                egress.EgressId);

            return new LiveKitEgressResult
            {
                EgressId = egress.EgressId,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start egress for room: {RoomName}", roomName);
            throw;
        }
    }

    public async Task<LiveKitHlsEgressResult> StartRoomCompositeHlsEgressAsync(
        string roomName,
        string playlistName,
        string filePath,
        uint segmentDuration = 6,
        int segmentCount = 0,
        string? layout = null)
    {
        try
        {
            var request = new RoomCompositeEgressRequest
            {
                RoomName = roomName,
                Layout = layout ?? "default",
            };

            var segmentOutput = new SegmentedFileOutput
            {
                PlaylistName = playlistName,
                FilenamePrefix = filePath,
                SegmentDuration = segmentDuration,
            };

            request.SegmentOutputs.Add(segmentOutput);

            var egress = await _egressService.StartRoomCompositeEgress(request);
            _logger.LogInformation("Started HLS egress for room: {RoomName}, egressId: {EgressId}", roomName,
                egress.EgressId);

            return new LiveKitHlsEgressResult
            {
                EgressId = egress.EgressId,
                PlaylistName = playlistName,
                FilenamePrefix = filePath,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HLS egress for room: {RoomName}", roomName);
            throw;
        }
    }

    public async Task StopEgressAsync(string egressId)
    {
        try
        {
            await _egressService.StopEgress(new StopEgressRequest
            {
                EgressId = egressId
            });
            _logger.LogInformation("Stopped egress: {EgressId}", egressId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop egress: {EgressId}", egressId);
            throw;
        }
    }

    public string GenerateToken(
        string roomName,
        string identity,
        string? name = null,
        bool canPublish = false,
        bool canSubscribe = true,
        bool canPublishData = false,
        Dictionary<string, string>? metadata = null,
        TimeSpan? ttl = null)
    {
        var token = _accessToken
            .WithIdentity(identity);

        if (!string.IsNullOrEmpty(name))
        {
            token = token.WithName(name);
        }

        var grants = new VideoGrants
        {
            RoomJoin = true,
            Room = roomName,
            CanPublish = canPublish,
            CanSubscribe = canSubscribe,
            CanPublishData = canPublishData,
        };

        token = token.WithGrants(grants);

        if (metadata is { Count: > 0 })
            token = token.WithMetadata(JsonSerializer.Serialize(metadata));

        token = token.WithTtl(ttl ?? TimeSpan.FromHours(4));

        return token.ToJwt();
    }

    public async Task<RoomDetailsResult?> GetRoomDetailsAsync(string roomName)
    {
        try
        {
            var room = await _roomService.ListParticipants(new ListParticipantsRequest
            {
                Room = roomName
            });

            return new RoomDetailsResult
            {
                ParticipantCount = room.Participants.Count,
                Participants = room.Participants.Select(p => new ParticipantInfo
                {
                    Identity = p.Identity,
                    Name = p.Name,
                    State = p.State.ToString(),
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get room details for: {RoomName}", roomName);
            return null;
        }
    }

    public async Task SendDataAsync(string roomName, byte[] data, bool reliable = true)
    {
        try
        {
            try
            {
                var participants = await _roomService.ListParticipants(new ListParticipantsRequest { Room = roomName });
                _logger.LogInformation("Room {RoomName} has {Count} participants: {Identities}", 
                    roomName, 
                    participants.Participants.Count,
                    string.Join(", ", participants.Participants.Select(p => p.Identity)));
                
                if (participants.Participants.Count == 0)
                {
                    _logger.LogDebug("Room {RoomName} has no participants, skipping send data", roomName);
                    return;
                }
            }
            catch
            {
                _logger.LogDebug("Room {RoomName} does not exist or error checking participants, skipping send data", roomName);
                return;
            }

            var request = new SendDataRequest
            {
                Room = roomName,
                Data = Google.Protobuf.ByteString.CopyFrom(data),
                Kind = reliable ? DataPacket.Types.Kind.Reliable : DataPacket.Types.Kind.Lossy,
            };

            _logger.LogInformation("Sending data to room {RoomName}: {Data}", roomName, System.Text.Encoding.UTF8.GetString(data));

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    await _roomService.SendData(request);
                    _logger.LogDebug("Sent data to room: {RoomName}, size: {Size} bytes", roomName, data.Length);
                    return;
                }
                catch (Exception ex) when (i < 2)
                {
                    _logger.LogWarning(ex, "Retry {Attempt} sending data to room: {RoomName}", i + 1, roomName);
                    await Task.Delay(100 * (i + 1));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send data to room after retries: {RoomName}", roomName);
        }
    }

    public async Task SendDataToParticipantAsync(string roomName, string identity, byte[] data, bool reliable = true)
    {
        try
        {
            var request = new SendDataRequest
            {
                Room = roomName,
                Data = Google.Protobuf.ByteString.CopyFrom(data),
                Kind = reliable ? DataPacket.Types.Kind.Reliable : DataPacket.Types.Kind.Lossy,
                DestinationIdentities = { identity },
            };

            await _roomService.SendData(request);
            _logger.LogDebug("Sent data to participant {Identity} in room: {RoomName}", identity, roomName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send data to participant {Identity} in room: {RoomName}", identity, roomName);
            throw;
        }
    }

    public async Task BroadcastLivestreamUpdateAsync(string roomName, string eventType, Dictionary<string, object>? data = null)
    {
        try
        {
            var payload = new Dictionary<string, object>
            {
                { "type", eventType }
            };

            if (data != null)
            {
                foreach (var item in data)
                {
                    payload[item.Key] = item.Value;
                }
            }

            var json = JsonSerializer.Serialize(payload);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);

            await SendDataAsync(roomName, bytes);
            _logger.LogInformation("Broadcast livestream update: {EventType} to room: {RoomName}", eventType, roomName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast livestream update to room: {RoomName}", roomName);
        }
    }
}

public record LiveKitRoomResult
{
    public string RoomName { get; init; } = string.Empty;
    public string Sid { get; init; } = string.Empty;
}

public record LiveKitIngressResult
{
    public string IngressId { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string StreamKey { get; init; } = string.Empty;
}

public record LiveKitEgressResult
{
    public string EgressId { get; init; } = string.Empty;
}

public record RoomDetailsResult
{
    public int ParticipantCount { get; init; }
    public List<ParticipantInfo> Participants { get; init; } = [];
}

public record ParticipantInfo
{
    public string Identity { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
}

public record LiveKitHlsEgressResult
{
    public string EgressId { get; init; } = string.Empty;
    public string PlaylistName { get; init; } = string.Empty;
    public string FilenamePrefix { get; init; } = string.Empty;
}