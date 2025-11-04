using DysonNetwork.Shared.Models;

namespace DysonNetwork.Pass.Account.Presences;

/// <summary>
/// Interface for presence services that can update user presence activities
/// </summary>
public interface IPresenceService
{
    /// <summary>
    /// The unique identifier for this presence service (e.g., "spotify", "discord")
    /// </summary>
    string ServiceId { get; }

    /// <summary>
    /// Updates presence activities for the specified users
    /// </summary>
    /// <param name="userIds">The user IDs to update presence for</param>
    /// <returns>A task representing the asynchronous operation</returns>
    Task UpdatePresencesAsync(IEnumerable<Guid> userIds);
}
