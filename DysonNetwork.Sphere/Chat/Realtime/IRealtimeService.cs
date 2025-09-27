using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Sphere.Chat.Realtime;

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
    /// Processes incoming webhook requests from the realtime service provider
    /// </summary>
    /// <param name="body">The webhook request body content</param>
    /// <param name="authHeader">The authentication header value</param>
    /// <returns>Task representing the asynchronous operation</returns>
    Task ReceiveWebhook(string body, string authHeader);
    
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