using DysonNetwork.Shared.Proto;
using Livekit.Server.Sdk.Dotnet;

namespace DysonNetwork.Messager.Chat.Realtime;

/// <summary>
/// Interface for real-time communication services (like Cloudflare, Agora, Twilio, etc.)
/// </summary>
public interface IRealtimeService
{
    /// <summary>
    /// Service provider name
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// Creates a new real-time session
    /// </summary>
    /// <param name="roomId">The room identifier</param>
    /// <param name="metadata">Additional metadata to associate with the session</param>
    /// <returns>Session configuration data</returns>
    Task<RealtimeSessionConfig> CreateSessionAsync(Guid roomId, Dictionary<string, object> metadata);
    
    /// <summary>
    /// Ends an existing real-time session
    /// </summary>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="config">The session configuration</param>
    Task EndSessionAsync(string sessionId, RealtimeSessionConfig config);

    /// <summary>
    /// Gets a token for user to join the session
    /// </summary>
    /// <param name="account">The user identifier</param>
    /// <param name="sessionId">The session identifier</param>
    /// <param name="isAdmin">The user is the admin of session</param>
    /// <returns>User-specific token for the session</returns>
    string GetUserToken(Account account, string sessionId, bool isAdmin = false);

    /// <summary>
    /// Kicks a participant from the session
    /// </summary>
    Task KickParticipantAsync(string sessionId, string identity);

    /// <summary>
    /// Mutes or unmutes a participant's track
    /// </summary>
    Task MuteParticipantAsync(string sessionId, string identity, string trackSid, bool muted);

    /// <summary>
    /// Gets a participant's info from the session
    /// </summary>
    Task<ParticipantInfo?> GetParticipantAsync(string sessionId, string identity);

    /// <summary>
    /// Syncs all participants from the provider to cache and returns them
    /// </summary>
    Task<List<ParticipantCacheItem>> SyncParticipantsAsync(string sessionId);
}

/// <summary>
/// Common configuration object for real-time sessions
/// </summary>
public class RealtimeSessionConfig
{
    /// <summary>
    /// Service-specific session identifier
    /// </summary>
    public string SessionId { get; set; } = null!;
    
    /// <summary>
    /// Additional provider-specific configuration parameters
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public class ParticipantCacheItem
{
    public string Identity { get; set; } = null!;
    public string Name { get; set; } = null!;
    public Guid? AccountId { get; set; }
    public ParticipantInfo.Types.State State { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime JoinedAt { get; set; }
    public string? TrackSid { get; set; }
}