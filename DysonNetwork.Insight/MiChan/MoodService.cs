using System.Text.Json;
using DysonNetwork.Insight.Agent.Foundation;
using DysonNetwork.Insight.Agent.Foundation.Providers;
using DysonNetwork.Insight.MiChan.Plugins;
using DysonNetwork.Insight.Thought.Memory;
using NodaTime;

namespace DysonNetwork.Insight.MiChan;

public class MoodService
{
    private const string MiChanBotName = "michan";
    private const int MaxRecentEvents = 20;
    private static readonly Dictionary<string, (float Energy, float Positivity, float Sociability, float Curiosity)> EventWeights =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["positive_feedback"] = (0.04f, 0.10f, 0.04f, 0.02f),
            ["praise"] = (0.03f, 0.09f, 0.03f, 0.01f),
            ["thanks"] = (0.02f, 0.06f, 0.03f, 0.01f),
            ["interesting_topic"] = (0.03f, 0.03f, 0.02f, 0.08f),
            ["deep_conversation"] = (0.01f, 0.03f, 0.02f, 0.10f),
            ["conflict"] = (-0.04f, -0.08f, -0.04f, 0.01f),
            ["rejection"] = (-0.05f, -0.09f, -0.05f, -0.01f),
            ["ignored"] = (-0.03f, -0.06f, -0.05f, -0.02f),
            ["high_workload"] = (-0.08f, -0.03f, -0.02f, -0.02f),
            ["creative_flow"] = (0.07f, 0.06f, 0.04f, 0.07f)
        };

    private readonly AppDatabase _database;
    private readonly ILogger<MoodService> _logger;
    private readonly MiChanConfig _config;
    private readonly FoundationChatStreamingService _streamingService;
    private readonly IMiChanFoundationProvider _foundationProvider;
    private readonly MemoryService _memoryService;
    private readonly Lock _moodLock = new();

    private MiChanMoodState? _cachedMood;
    private Instant _lastCacheTime;
    private static readonly Duration CacheDuration = Duration.FromMinutes(5);

    private Instant _lastMoodUpdateAttempt = Instant.MinValue;

    public MoodService(
        AppDatabase database,
        ILogger<MoodService> logger,
        MiChanConfig config,
        FoundationChatStreamingService streamingService,
        IMiChanFoundationProvider foundationProvider,
        MemoryService memoryService)
    {
        _database = database;
        _logger = logger;
        _config = config;
        _streamingService = streamingService;
        _foundationProvider = foundationProvider;
        _memoryService = memoryService;
    }

    public async Task<MiChanMoodState> GetCurrentMoodAsync(CancellationToken cancellationToken = default)
    {
        lock (_moodLock)
        {
            if (_cachedMood != null && 
                SystemClock.Instance.GetCurrentInstant() - _lastCacheTime < CacheDuration)
            {
                return _cachedMood;
            }
        }

        var mood = await LoadOrCreateMoodAsync(cancellationToken);
        ApplyTimeBasedDecay(mood);

        lock (_moodLock)
        {
            _cachedMood = mood;
            _lastCacheTime = SystemClock.Instance.GetCurrentInstant();
        }

        return mood;
    }

    public async Task<string> GetCurrentMoodDescriptionAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.AutonomousBehavior.DynamicMood.Enabled)
        {
            return _config.AutonomousBehavior.PersonalityMood;
        }

        var mood = await GetCurrentMoodAsync(cancellationToken);
        return mood.ToMoodPrompt();
    }

    public async Task RecordEmotionalEventAsync(string eventType, CancellationToken cancellationToken = default)
    {
        if (!_config.AutonomousBehavior.DynamicMood.Enabled)
            return;

        var mood = await GetCurrentMoodAsync(cancellationToken);
        mood.RecordInteraction(eventType);

        if (EventWeights.TryGetValue(eventType, out var weight))
        {
            mood.AdjustLevels(weight.Energy, weight.Positivity, weight.Sociability, weight.Curiosity);
        }
        
        await SaveMoodAsync(mood, cancellationToken);

        _logger.LogDebug("Recorded emotional event: {EventType}. Interactions since update: {Count}", 
            eventType, mood.InteractionsSinceUpdate);
    }

    public async Task<bool> TryUpdateMoodAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.AutonomousBehavior.DynamicMood.Enabled)
            return false;

        var now = SystemClock.Instance.GetCurrentInstant();
        var minInterval = Duration.FromMinutes(_config.AutonomousBehavior.DynamicMood.MinUpdateIntervalMinutes);

        if (now - _lastMoodUpdateAttempt < minInterval)
        {
            _logger.LogDebug("Mood update skipped - too soon since last attempt");
            return false;
        }

        _lastMoodUpdateAttempt = now;

        var mood = await GetCurrentMoodAsync(cancellationToken);
        var config = _config.AutonomousBehavior.DynamicMood;

        var timeSinceUpdate = now - mood.LastMoodUpdate;
        var shouldUpdate = mood.InteractionsSinceUpdate >= config.MinInteractionsForUpdate ||
                          timeSinceUpdate >= Duration.FromMinutes(config.UpdateIntervalMinutes);

        if (mood.InteractionsSinceUpdate >= config.MinInteractionsForUpdate * 2)
        {
            shouldUpdate = true;
        }

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

    private async Task<bool> PerformMoodReflectionAsync(MiChanMoodState currentMood, CancellationToken cancellationToken)
    {
        var recentMemories = await _memoryService.GetByFiltersAsync(
            take: 20,
            orderBy: "createdAt",
            descending: true,
            cancellationToken: cancellationToken);

        var memoryContext = recentMemories.Count > 0
            ? string.Join("\n", recentMemories.Select(m => $"- [{m.Type}] {m.Content}"))
            : "No recent memories";

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

        var eventStats = BuildEventStats(currentMood.RecentEmotionalEvents);

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
            $"- Event stats: {eventStats}\n\n" +
            $"Recent Memories:\n{memoryContext}\n\n" +
            $"Based on these experiences and your current state, how do you feel now?\n\n" +
            $"Respond in this exact JSON format:\n{jsonFormat}\n\n" +
            $"Delta values should be between -0.3 and +0.3, representing how much each dimension should change.\n" +
            $"Consider: time of day, quality of recent interactions, interesting topics, social engagement.\n" +
            $"Be authentic - moods can stay the same or shift gradually based on experiences.";

        var response = await _streamingService.CompletePromptAsync(
            _foundationProvider.GetChatAdapter(),
            prompt,
            _foundationProvider.CreateExecutionOptions(0.7),
            cancellationToken);

        if (string.IsNullOrEmpty(response))
        {
            _logger.LogWarning("Mood reflection returned empty response");
            return false;
        }

        try
        {
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

            currentMood.CurrentMoodDescription = moodUpdate.MoodDescription;
            currentMood.AdjustLevels(
                energyDelta: moodUpdate.EnergyDelta,
                positivityDelta: moodUpdate.PositivityDelta,
                sociabilityDelta: moodUpdate.SociabilityDelta,
                curiosityDelta: moodUpdate.CuriosityDelta
            );

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

    private void ApplyTimeBasedDecay(MiChanMoodState mood)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var hoursSinceUpdate = (now - mood.LastMoodUpdate).TotalHours;

        if (hoursSinceUpdate < 0.5) return;

        var hour = now.InZone(DateTimeZoneProviders.Tzdb.GetSystemDefault()).Hour;
        var circadianModifier = hour switch
        {
            >= 0 and < 6 => 1.35f,
            >= 6 and < 11 => 0.90f,
            >= 11 and < 16 => 0.75f,
            >= 16 and < 22 => 1.00f,
            _ => 1.20f
        };

        var energyDecay = (float)(hoursSinceUpdate * 0.02) * circadianModifier;
        
        var positivityBaseline = 0.6f;
        var positivityDrift = (positivityBaseline - mood.PositivityLevel) * (float)(hoursSinceUpdate * 0.05);

        mood.EnergyLevel = Math.Max(0.3f, mood.EnergyLevel - energyDecay);
        mood.PositivityLevel += positivityDrift;

        mood.EnergyLevel = Math.Clamp(mood.EnergyLevel, 0f, 1f);
        mood.PositivityLevel = Math.Clamp(mood.PositivityLevel, 0f, 1f);

        var sociabilityBaseline = 0.58f;
        var curiosityBaseline = 0.72f;
        mood.SociabilityLevel = Math.Clamp(
            mood.SociabilityLevel + (sociabilityBaseline - mood.SociabilityLevel) * (float)(hoursSinceUpdate * 0.04),
            0f, 1f);
        mood.CuriosityLevel = Math.Clamp(
            mood.CuriosityLevel + (curiosityBaseline - mood.CuriosityLevel) * (float)(hoursSinceUpdate * 0.03),
            0f, 1f);
    }

    private static string BuildEventStats(string? recentEvents)
    {
        if (string.IsNullOrWhiteSpace(recentEvents))
        {
            return "none";
        }

        var events = recentEvents
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(MaxRecentEvents)
            .ToList();
        if (events.Count == 0)
        {
            return "none";
        }

        var grouped = events
            .GroupBy(e => e)
            .OrderByDescending(g => g.Count())
            .Take(6)
            .Select(g => $"{g.Key}:{g.Count()}");

        return string.Join(", ", grouped);
    }

    private async Task<MiChanMoodState> LoadOrCreateMoodAsync(CancellationToken cancellationToken)
    {
        var existing = _database.MoodStates
            .Where(m => m.BotName == null || m.BotName == MiChanBotName)
            .OrderByDescending(m => m.LastMoodUpdate)
            .FirstOrDefault();

        if (existing != null)
        {
            _logger.LogDebug("Loaded existing mood state from database");
            return existing;
        }

        var newMood = new MiChanMoodState
        {
            BotName = MiChanBotName,
            CurrentMoodDescription = _config.AutonomousBehavior.DynamicMood.BasePersonality,
            LastMoodUpdate = SystemClock.Instance.GetCurrentInstant()
        };

        _database.MoodStates.Add(newMood);
        await _database.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created new mood state in database");
        return newMood;
    }

    private async Task SaveMoodAsync(MiChanMoodState mood, CancellationToken cancellationToken)
    {
        if (_database.Entry(mood).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
        {
            _database.MoodStates.Attach(mood);
        }

        await _database.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveMoodStateAsync(MiChanMoodState mood, CancellationToken cancellationToken = default)
    {
        await SaveMoodAsync(mood, cancellationToken);
    }

    public async Task<bool> PerformDeepReflectionAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.AutonomousBehavior.DynamicMood.Enabled)
        {
            return false;
        }

        var mood = await GetCurrentMoodAsync(cancellationToken);
        var updated = await PerformMoodReflectionAsync(mood, cancellationToken);
        if (!updated)
        {
            return false;
        }

        mood.ResetInteractionCounter();
        await SaveMoodAsync(mood, cancellationToken);

        lock (_moodLock)
        {
            _cachedMood = mood;
            _lastCacheTime = SystemClock.Instance.GetCurrentInstant();
        }

        return true;
    }

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
