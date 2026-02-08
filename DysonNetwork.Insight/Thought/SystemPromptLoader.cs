namespace DysonNetwork.Insight.Thought;

public static class SystemPromptLoader
{
    private static string? _cachedPrompt;
    private static DateTime _lastReadTime;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(1);

    public static string LoadSystemPrompt(string? filePath, string defaultPrompt, ILogger logger)
    {
        // Return cached version if available and recent
        if (_cachedPrompt != null && DateTime.UtcNow - _lastReadTime < CacheDuration)
        {
            return _cachedPrompt;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            logger.LogDebug("No system prompt file specified, using default");
            _cachedPrompt = defaultPrompt;
            _lastReadTime = DateTime.UtcNow;
            return _cachedPrompt;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                logger.LogWarning("System prompt file not found at {FilePath}, using default", filePath);
                _cachedPrompt = defaultPrompt;
                _lastReadTime = DateTime.UtcNow;
                return _cachedPrompt;
            }

            var content = File.ReadAllText(filePath);
            
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("System prompt file at {FilePath} is empty, using default", filePath);
                _cachedPrompt = defaultPrompt;
                _lastReadTime = DateTime.UtcNow;
                return _cachedPrompt;
            }

            logger.LogInformation("Loaded system prompt from file: {FilePath} ({Length} characters)", filePath, content.Length);
            _cachedPrompt = content;
            _lastReadTime = DateTime.UtcNow;
            return _cachedPrompt;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading system prompt file at {FilePath}, using default", filePath);
            _cachedPrompt = defaultPrompt;
            _lastReadTime = DateTime.UtcNow;
            return _cachedPrompt;
        }
    }

    public static void ClearCache()
    {
        _cachedPrompt = null;
        _lastReadTime = DateTime.MinValue;
    }
}
