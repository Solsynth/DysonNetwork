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
        _roomService = new RoomServiceClient(host, apiKey, apiSecret);
        _ingressService = new IngressServiceClient(host, apiKey, apiSecret);
        _egressService = new EgressServiceClient(host, apiKey, apiSecret);
        _accessToken = new AccessToken(apiKey, apiSecret);
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

    public async Task<LiveKitIngressResult> CreateIngressAsync(string roomName, string participantIdentity, string? participantName = null, string? title = null)
    {
        try
        {
            var request = new CreateIngressRequest
            {
                InputType = IngressInput.RtmpInput,
                Name = title ?? roomName,
                RoomName = roomName,
                ParticipantIdentity = participantIdentity,
            };

            if (!string.IsNullOrEmpty(participantName))
            {
                request.ParticipantName = participantName;
            }

            var ingress = await _ingressService.CreateIngress(request);
            _logger.LogInformation("Created ingress for room: {RoomName}, ingressId: {IngressId}", roomName, ingress.IngressId);

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
            _logger.LogInformation("Started egress for room: {RoomName}, egressId: {EgressId}", roomName, egress.EgressId);

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

        if (metadata != null && metadata.Count > 0)
        {
            token = token.WithMetadata(JsonSerializer.Serialize(metadata));
        }

        if (ttl.HasValue)
        {
            token = token.WithTtl(ttl.Value);
        }
        else
        {
            token = token.WithTtl(TimeSpan.FromHours(4));
        }

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
