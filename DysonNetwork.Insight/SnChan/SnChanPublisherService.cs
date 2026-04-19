using DysonNetwork.Shared.Models;

namespace DysonNetwork.Insight.SnChan;

/// <summary>
/// Service for managing SnChan's dual publishers (personal and official)
/// </summary>
public class SnChanPublisherService
{
    private readonly SnChanApiClient _apiClient;
    private readonly SnChanConfig _config;
    private readonly ILogger<SnChanPublisherService> _logger;

    private bool _initialized = false;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SnChanPublisherService(
        SnChanApiClient apiClient,
        SnChanConfig config,
        ILogger<SnChanPublisherService> logger)
    {
        _apiClient = apiClient;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Gets the personal publisher ID (cached)
    /// </summary>
    public string? PersonalPublisherId => _config.PersonalPublisherId;

    /// <summary>
    /// Gets the official publisher ID (cached)
    /// </summary>
    public string? OfficialPublisherId => _config.OfficialPublisherId;

    /// <summary>
    /// Gets the personal publisher name
    /// </summary>
    public string PersonalPublisherName => _config.PersonalPublisherName;

    /// <summary>
    /// Gets the official publisher name
    /// </summary>
    public string OfficialPublisherName => _config.OfficialPublisherName;

    /// <summary>
    /// Initializes the service by loading publisher IDs from the API
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            _logger.LogInformation("Initializing SnChan publisher service...");

            // Load personal publisher
            var personalPublisher = await _apiClient.GetAsync<SnPublisher>(
                "sphere",
                $"/publishers/{_config.PersonalPublisherName}"
            );

            if (personalPublisher != null)
            {
                _config.PersonalPublisherId = personalPublisher.Id.ToString();
                _logger.LogInformation(
                    "Loaded personal publisher: {Name} (ID: {Id})",
                    personalPublisher.Name,
                    personalPublisher.Id
                );
            }
            else
            {
                _logger.LogWarning(
                    "Personal publisher '{Name}' not found",
                    _config.PersonalPublisherName
                );
            }

            // Load official publisher
            var officialPublisher = await _apiClient.GetAsync<SnPublisher>(
                "sphere",
                $"/publishers/{_config.OfficialPublisherName}"
            );

            if (officialPublisher != null)
            {
                _config.OfficialPublisherId = officialPublisher.Id.ToString();
                _logger.LogInformation(
                    "Loaded official publisher: {Name} (ID: {Id})",
                    officialPublisher.Name,
                    officialPublisher.Id
                );
            }
            else
            {
                _logger.LogWarning(
                    "Official publisher '{Name}' not found",
                    _config.OfficialPublisherName
                );
            }

            _initialized = true;
            _logger.LogInformation("SnChan publisher service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SnChan publisher service");
            throw;
        }
    }

    /// <summary>
    /// Ensures the service is initialized before use. Thread-safe.
    /// </summary>
    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (!_initialized)
            {
                await InitializeAsync(cancellationToken);
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Gets the appropriate publisher name for creating a post
    /// </summary>
    /// <param name="asOfficial">Whether to use the official publisher</param>
    /// <returns>The publisher name to use</returns>
    public string GetPublisherNameForPost(bool asOfficial = false)
    {
        return asOfficial ? _config.OfficialPublisherName : _config.PersonalPublisherName;
    }

    /// <summary>
    /// Gets the appropriate publisher ID for creating a post
    /// </summary>
    /// <param name="asOfficial">Whether to use the official publisher</param>
    /// <returns>The publisher ID to use, or null if not loaded</returns>
    public string? GetPublisherIdForPost(bool asOfficial = false)
    {
        return asOfficial ? _config.OfficialPublisherId : _config.PersonalPublisherId;
    }

    /// <summary>
    /// Determines if a post was created by SnChan (either publisher)
    /// </summary>
    public bool IsOwnPost(SnPost post)
    {
        if (post.PublisherId?.ToString() == _config.PersonalPublisherId)
        {
            return true;
        }

        if (post.PublisherId?.ToString() == _config.OfficialPublisherId)
        {
            return true;
        }

        // Check by publisher name
        if (post.Publisher?.Name?.Equals(_config.PersonalPublisherName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        if (post.Publisher?.Name?.Equals(_config.OfficialPublisherName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a post was created by the official publisher
    /// </summary>
    public bool IsOfficialPost(SnPost post)
    {
        if (post.PublisherId?.ToString() == _config.OfficialPublisherId)
        {
            return true;
        }

        if (post.Publisher?.Name?.Equals(_config.OfficialPublisherName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Determines if a post was created by the personal publisher
    /// </summary>
    public bool IsPersonalPost(SnPost post)
    {
        if (post.PublisherId?.ToString() == _config.PersonalPublisherId)
        {
            return true;
        }

        if (post.Publisher?.Name?.Equals(_config.PersonalPublisherName, StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets context about both publishers for AI decision making
    /// </summary>
    public string GetPublisherContext()
    {
        return $@"You have two publishers available:
1. Personal Publisher: '{_config.PersonalPublisherName}' (ID: {_config.PersonalPublisherId ?? "not loaded"})
   - Use for: casual conversations, diary entries, personal thoughts, most replies
   - This is your default personality

2. Official Publisher: '{_config.OfficialPublisherName}' (ID: {_config.OfficialPublisherId ?? "not loaded"})
   - Use for: official announcements, explaining Solar Network issues, support responses, formal statements
   - This represents the Solar Network/SolSynth team

When replying to posts, consider:
- If the post is from the official publisher, reply as official
- If the user is asking for help with Solar Network issues (@snchan mention), consider using official
- For casual conversations and most interactions, use personal";
    }
}
