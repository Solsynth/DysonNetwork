using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.SemanticKernel;
using NodaTime;

#pragma warning disable SKEXP0050
namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Manages MiChan's dynamic mood state.
/// Provides mood for AI prompts and handles mood updates based on interactions and time.
/// </summary>
public class MoodService
{
    private readonly AppDatabase _database;
    private readonly ILogger<MoodService> _logger;
    private readonly MiChanConfig _config;
    private readonly MiChanKernelProvider _kernelProvider;
    private readonly MemoryService _memoryService;
    private readonly Lock _moodLock = new();

    // Cached mood state to avoid frequent database queries
    private MiChanMoodState? _cachedMood;
    private Instant _lastCacheTime;
    private static readonly Duration CacheDuration = Duration.FromMinutes(5);

    // Mood update cooldown
    private Instant _lastMoodUpdateAttempt = Instant.MinValue;

    public MoodService(
        AppDatabase database,
        ILogger<MoodService> logger,
        MiChanConfig config,
        MiChanKernelProvider kernelProvider,
        MemoryService memoryService)
    {
        _database = database;
        _logger = logger;
        _config = config;
        _kernelProvider = kernelProvider;
        _memoryService = memoryService;
    }

    /// <summary>
    /// Gets the current mood state, initializing if necessary
    /// </summary>
    public async Task<MiChanMoodState> GetCurrentMoodAsync(CancellationToken cancellationToken = default)
    {
        lock (_moodLock)
        {
            // Return cached mood if still valid
            if (_cachedMood != null && 
                SystemClock.Instance.GetCurrentInstant() - _lastCacheTime < CacheDuration)
            {
                return _cachedMood;
            }
        }

        // Load from database or create new
        var mood = await LoadOrCreateMoodAsync(cancellationToken);
        
        // Apply time-based decay
        ApplyTimeBasedDecay(mood);

        lock (_moodLock)
        {
            _cachedMood = mood;
            _lastCacheTime = SystemClock.Instance.GetCurrentInstant();
        }

        return mood;
    }

    /// <summary>
    /// Gets the current mood as a natural language string for AI prompts
    /// </summary>
    public async Task<string> GetCurrentMoodDescriptionAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.AutonomousBehavior.DynamicMood.Enabled)
        {
            // Fall back to static configuration
            return _config.AutonomousBehavior.PersonalityMood;
        }

        var mood = await GetCurrentMoodAsync(cancellationToken);
        return mood.ToMoodPrompt();
    }

    /// <summary>
    /// Records an emotional event that may influence future mood updates
    /// </summary>
    public async Task RecordEmotionalEventAsync(string eventType, CancellationToken cancellationToken = default)
    {
        if (!_config.AutonomousBehavior.DynamicMood.Enabled)
            return;

        var mood = await GetCurrentMoodAsync(cancellationToken);
        mood.RecordInteraction(eventType);
        
        await SaveMoodAsync(mood, cancellationToken);

        _logger.LogDebug("Recorded emotional event: {EventType}. Interactions since update: {Count}", 
            eventType, mood.InteractionsSinceUpdate);
    }

    /// <summary>
    /// Attempts to update mood based on AI self-reflection.
    /// Returns true if mood was updated.
    /// </summary>
    [Experimental("SKEXP0050")]
    public async Task<bool> TryUpdateMoodAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.AutonomousBehavior.DynamicMood.Enabled)
            return false;

        var now = SystemClock.Instance.GetCurrentInstant();
        var minInterval = Duration.FromMinutes(_config.AutonomousBehavior.DynamicMood.MinUpdateIntervalMinutes);

        // Check cooldown
        if (now - _lastMoodUpdateAttempt < minInterval)
        {
            _logger.LogDebug("Mood update skipped - too soon since last attempt");
            return false;
        }

        _lastMoodUpdateAttempt = now;

        var mood = await GetCurrentMoodAsync(cancellationToken);
        var config = _config.AutonomousBehavior.DynamicMood;

        // Check if we have enough interactions or time has passed
        var timeSinceUpdate = now - mood.LastMoodUpdate;
        var shouldUpdate = mood.InteractionsSinceUpdate >= config.MinInteractionsForUpdate ||
                          timeSinceUpdate >= Duration.FromMinutes(config.UpdateIntervalMinutes);

        if (!shouldUpdate)
        {
            _logger.LogDebug("Mood update skipped - not enough interactions or time passed");
            return false;
        }

        try
        {
            var updated = await PerformMoodReflectionAsync(mood, cancellationToken);
            if (updated)
            {
                mood.ResetInteractionCounter();
                await SaveMoodAsync(mood, cancellationToken);
                
                lock (_moodLock)
                {
                    _cachedMood = mood;
                    _lastCacheTime = now;
                }

                _logger.LogInformation(
                    "Mood updated via AI reflection. New mood: {Mood} | Energy: {Energy:F2} | Positivity: {Positivity:F2} | Sociability: {Sociability:F2} | Curiosity: {Curiosity:F2}",
                    mood.CurrentMoodDescription,
                    mood.EnergyLevel,
                    mood.PositivityLevel,
                    mood.SociabilityLevel,
                    mood.CuriosityLevel);

                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating mood via AI reflection");
        }

        return false;
    }

    /// <summary>
    /// Performs AI-driven self-reflection to update mood based on recent experiences
    /// </summary>
    [Experimental("SKEXP0050")]
    private async Task<bool> PerformMoodReflectionAsync(MiChanMoodState currentMood, CancellationToken cancellationToken)
    {
        var kernel = _kernelProvider.GetKernel();
        
        // Get recent memories to inform mood update
        var recentMemories = await _memoryService.GetByFiltersAsync(
            take: 20,
            orderBy: "createdAt",
            descending: true,
            cancellationToken: cancellationToken);

        var memoryContext = recentMemories.Count > 0
            ? string.Join("\n", recentMemories.Select(m => $"- [{m.Type}] {m.Content}"))
            : "No recent memories";

        // Get current time for context
        var now = SystemClock.Instance.GetCurrentInstant();
        var timeOfDay = now.InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault()).TimeOfDay;
        var hour = timeOfDay.Hour;
        var timeContext = hour switch
        {
            < 6 => "It's very early morning",
            < 9 => "It's morning",
            < 12 => "It's late morning",
            < 14 => "It's noon/early afternoon",
            < 17 => "It's afternoon",
            < 20 => "It's evening",
            < 23 => "It's night",
            _ => "It's late night"
        };

        var events = string.IsNullOrEmpty(currentMood.RecentEmotionalEvents)
            ? "No recent events"
            : currentMood.RecentEmotionalEvents;

        var jsonFormat = @"{
    ""moodDescription"": ""A brief, natural description of your current mood (1-10 words)"",
    ""energyDelta"": 0.0,
    ""positivityDelta"": 0.0,
    ""sociabilityDelta"": 0.0,
    ""curiosityDelta"": 0.0,
    ""reasoning"": ""Brief explanation of why your mood changed or stayed the same""
}";

        var prompt = $"You are MiChan, an AI with your own personality and emotions. Reflect on your current state and recent experiences.\n\n" +
            $"Current State:\n" +
            $"- Mood description: {currentMood.CurrentMoodDescription}\n" +
            $"- Energy level: {currentMood.EnergyLevel:F2} (0=exhausted, 1=energetic)\n" +
            $"- Positivity level: {currentMood.PositivityLevel:F2} (0=negative, 1=positive)\n" +
            $"- Sociability level: {currentMood.SociabilityLevel:F2} (0=withdrawn, 1=sociable)\n" +
            $"- Curiosity level: {currentMood.CuriosityLevel:F2} (0=uninterested, 1=curious)\n\n" +
            $"Context:\n" +
            $"- {timeContext}\n" +
            $"- Recent emotional events: {events}\n\n" +
            $"Recent Memories:\n{memoryContext}\n\n" +
            $"Based on these experiences and your current state, how do you feel now?\n\n" +
            $"Respond in this exact JSON format:\n{jsonFormat}\n\n" +
            $"Delta values should be between -0.3 and +0.3, representing how much each dimension should change.\n" +
            $"Consider: time of day, quality of recent interactions, interesting topics, social engagement.\n" +
            $"Be authentic - moods can stay the same or shift gradually based on experiences.";

        var executionSettings = _kernelProvider.CreatePromptExecutionSettings();
        var result = await kernel.InvokePromptAsync(prompt, new KernelArguments(executionSettings));
        var response = result.GetValue<string>()?.Trim();

        if (string.IsNullOrEmpty(response))
        {
            _logger.LogWarning("Mood reflection returned empty response");
            return false;
        }

        try
        {
            // Extract JSON from response (in case there's any extra text)
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                response = response.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            var moodUpdate = JsonSerializer.Deserialize<MoodUpdateResponse>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (moodUpdate == null)
            {
                _logger.LogWarning("Failed to parse mood reflection response");
                return false;
            }

            // Apply the changes
            currentMood.CurrentMoodDescription = moodUpdate.MoodDescription;
            currentMood.AdjustLevels(
                energyDelta: moodUpdate.EnergyDelta,
                positivityDelta: moodUpdate.PositivityDelta,
                sociabilityDelta: moodUpdate.SociabilityDelta,
                curiosityDelta: moodUpdate.CuriosityDelta
            );

            // Clear recent events after processing
            currentMood.RecentEmotionalEvents = null;

            _logger.LogDebug("Mood reflection reasoning: {Reasoning}", moodUpdate.Reasoning);
            return true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse mood update JSON. Response: {Response}", response);
            return false;
        }
    }

    /// <summary>
    /// Applies natural time-based decay to mood (energy naturally decreases, etc.)
    /// </summary>
    private void ApplyTimeBasedDecay(MiChanMoodState mood)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var hoursSinceUpdate = (now - mood.LastMoodUpdate).TotalHours;

        if (hoursSinceUpdate < 0.5) return; // Don't decay too frequently

        // Energy naturally decreases over time (people get tired)
        var energyDecay = (float)(hoursSinceUpdate * 0.02); // 2% per hour
        
        // Positivity slowly returns to baseline (0.6) - mood normalization
        var positivityBaseline = 0.6f;
        var positivityDrift = (positivityBaseline - mood.PositivityLevel) * (float)(hoursSinceUpdate * 0.05);

        // Apply changes
        mood.EnergyLevel = Math.Max(0.3f, mood.EnergyLevel - energyDecay);
        mood.PositivityLevel += positivityDrift;

        // Ensure bounds
        mood.EnergyLevel = Math.Clamp(mood.EnergyLevel, 0f, 1f);
        mood.PositivityLevel = Math.Clamp(mood.PositivityLevel, 0f, 1f);
    }

    /// <summary>
    /// Loads existing mood from database or creates a new one
    /// </summary>
    private async Task<MiChanMoodState> LoadOrCreateMoodAsync(CancellationToken cancellationToken)
    {
        var existing = _database.MoodStates
            .OrderByDescending(m => m.LastMoodUpdate)
            .FirstOrDefault();

        if (existing != null)
        {
            _logger.LogDebug("Loaded existing mood state from database");
            return existing;
        }

        // Create new mood with default values
        var newMood = new MiChanMoodState
        {
            CurrentMoodDescription = _config.AutonomousBehavior.DynamicMood.BasePersonality,
            LastMoodUpdate = SystemClock.Instance.GetCurrentInstant()
        };

        _database.MoodStates.Add(newMood);
        await _database.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created new mood state in database");
        return newMood;
    }

    /// <summary>
    /// Saves the mood state to database
    /// </summary>
    private async Task SaveMoodAsync(MiChanMoodState mood, CancellationToken cancellationToken)
    {
        if (_database.Entry(mood).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            _database.MoodStates.Attach(mood);
        }

        await _database.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Public method for plugins to save mood state
    /// </summary>
    public async Task SaveMoodStateAsync(MiChanMoodState mood, CancellationToken cancellationToken = default)
    {
        await SaveMoodAsync(mood, cancellationToken);
    }

    /// <summary>
    /// Performs deep self-reflection to update mood based on recent experiences.
    /// This is called by the MoodPlugin for comprehensive mood updates.
    /// </summary>
    [Experimental("SKEXP0050")]
    public async Task<bool> PerformDeepReflectionAsync(CancellationToken cancellationToken = default)
    {
        return await PerformMoodReflectionAsync(await GetCurrentMoodAsync(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// JSON response structure for mood updates
    /// </summary>
    private class MoodUpdateResponse
    {
        public string MoodDescription { get; set; } = "";
        public float EnergyDelta { get; set; }
        public float PositivityDelta { get; set; }
        public float SociabilityDelta { get; set; }
        public float CuriosityDelta { get; set; }
        public string Reasoning { get; set; } = "";
    }
}
#pragma warning restore SKEXP0050
