namespace DysonNetwork.Insight.MiChan;

public static class PersonalityLoader
{
    public static string LoadPersonality(string? filePath, string fallbackPersonality, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            logger.LogDebug("No personality file specified, using configuration value");
            return fallbackPersonality;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                logger.LogWarning("Personality file not found at {FilePath}, using configuration value", filePath);
                return fallbackPersonality;
            }

            var content = File.ReadAllText(filePath);
            
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("Personality file at {FilePath} is empty, using configuration value", filePath);
                return fallbackPersonality;
            }

            logger.LogInformation("Loaded personality from file: {FilePath} ({Length} characters)", filePath, content.Length);
            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading personality file at {FilePath}, using configuration value", filePath);
            return fallbackPersonality;
        }
    }

    public static string LoadPersonalityWithHotReload(string? filePath, string fallbackPersonality, ILogger logger)
    {
        var personality = LoadPersonality(filePath, fallbackPersonality, logger);
        
        // If file exists and was loaded, log that hot reload is available
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            logger.LogInformation("Personality file is configured at {FilePath}. Changes will be loaded on next AI interaction.", filePath);
        }
        
        return personality;
    }
}
