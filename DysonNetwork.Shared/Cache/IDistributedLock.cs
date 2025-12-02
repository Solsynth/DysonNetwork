namespace DysonNetwork.Shared.Cache;

/// <summary>
/// Represents a distributed lock that can be used to synchronize access across multiple processes
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    /// <summary>
    /// The resource identifier this lock is protecting
    /// </summary>
    string Resource { get; }

    /// <summary>
    /// Unique identifier for this lock instance
    /// </summary>
    string LockId { get; }

    /// <summary>
    /// Releases the lock immediately
    /// </summary>
    ValueTask ReleaseAsync();
}
