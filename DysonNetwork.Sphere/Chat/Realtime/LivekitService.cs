using Livekit.Server.Sdk.Dotnet;

namespace DysonNetwork.Sphere.Chat.Realtime;

/// <summary>
/// LiveKit implementation of the real-time communication service
/// </summary>
public class LivekitRealtimeService : IRealtimeService
{
    private readonly ILogger<LivekitRealtimeService> _logger;
    private readonly RoomServiceClient _roomService;
    private readonly AccessToken _accessToken;

    public LivekitRealtimeService(IConfiguration configuration, ILogger<LivekitRealtimeService> logger)
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
                Metadata = System.Text.Json.JsonSerializer.Serialize(roomMetadata)
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
}