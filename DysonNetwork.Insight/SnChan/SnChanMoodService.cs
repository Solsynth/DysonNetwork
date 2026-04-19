using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Thought;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using NodaTime;
using NodaTime.Extensions;

#pragma warning disable SKEXP0050
namespace DysonNetwork.Insight.SnChan;

public class SnChanMoodService
{
    private readonly AppDatabase _database;
    private readonly ILogger<SnChanMoodService> _logger;
    private readonly SnChanConfig _config;
    private readonly MemoryService _memoryService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Lock _moodLock = new();
    private const string SnChanBotName = "snchan";

    private MiChanMoodState? _cachedMood;
    private Instant _lastCacheTime;
    private static readonly Duration CacheDuration = Duration.FromMinutes(5);
    private Instant _lastMoodUpdateAttempt = Instant.MinValue;

    public SnChanMoodService(
        AppDatabase database,
        ILogger<SnChanMoodService> logger,
        SnChanConfig config,
        MemoryService memoryService,
        IServiceProvider serviceProvider)
    {
        _database = database;
        _logger = logger;
        _config = config;
        _memoryService = memoryService;
        _serviceProvider = serviceProvider;
    }

    public async Task<MiChanMoodState> GetCurrentMoodAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.DynamicMood.Enabled)
        {
            return CreateDefaultMood();
        }

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
        if (!_config.DynamicMood.Enabled)
        {
            return _config.DynamicMood.BasePersonality;
        }

        var mood = await GetCurrentMoodAsync(cancellationToken);
        return mood.ToMoodPrompt();
    }

    public async Task RecordInteractionAsync(string eventType, CancellationToken cancellationToken = default)
    {
        if (!_config.DynamicMood.Enabled)
        {
            return;
        }

        var mood = await GetCurrentMoodAsync(cancellationToken);
        mood.RecordInteraction(eventType);
        await _database.SaveChangesAsync(cancellationToken);
        
        _logger.LogDebug("Recorded emotional event for SnChan: {EventType}", eventType);
    }

    public async Task UpdateMoodAsync(
        float? energyDelta = null,
        float? positivityDelta = null,
        float? sociabilityDelta = null,
        float? curiosityDelta = null,
        CancellationToken cancellationToken = default)
    {
        if (!_config.DynamicMood.Enabled)
        {
            return;
        }

        var mood = await GetCurrentMoodAsync(cancellationToken);

        if (energyDelta.HasValue || positivityDelta.HasValue || sociabilityDelta.HasValue || curiosityDelta.HasValue)
        {
            mood.AdjustLevels(
                energyDelta ?? 0,
                positivityDelta ?? 0,
                sociabilityDelta ?? 0,
                curiosityDelta ?? 0);
            
            await _database.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation(
                "SnChan mood updated: energy={Energy}, positivity={Positivity}, sociability={Sociability}, curiosity={Curiosity}",
                mood.EnergyLevel, mood.PositivityLevel, mood.SociabilityLevel, mood.CuriosityLevel);
        }
    }

    /// <summary>
    /// Attempts to update mood through AI self-reflection if conditions are met.
    /// </summary>
    public async Task<bool> TryUpdateMoodAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.DynamicMood.Enabled)
        {
            return false;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var mood = await GetCurrentMoodAsync(cancellationToken);
        
        var timeSinceUpdate = now - mood.LastMoodUpdate;
        var timeSinceAttempt = now - _lastMoodUpdateAttempt;

        // Check cooldown
        var minInterval = Duration.FromMinutes(_config.DynamicMood.MinUpdateIntervalMinutes);
        if (timeSinceAttempt < minInterval)
        {
            _logger.LogDebug("Mood update skipped: cooldown not passed ({Minutes} min since last attempt)", 
                timeSinceAttempt.TotalMinutes);
            return false;
        }

        // Check if enough interactions or enough time has passed
        var updateInterval = Duration.FromMinutes(_config.DynamicMood.UpdateIntervalMinutes);
        var hasEnoughInteractions = mood.InteractionsSinceUpdate >= _config.DynamicMood.MinInteractionsForUpdate;
        var hasEnoughTime = timeSinceUpdate >= updateInterval;

        if (!hasEnoughInteractions && !hasEnoughTime)
        {
            _logger.LogDebug("Mood update skipped: not enough interactions ({Interactions}/{MinInteractions}) and not enough time ({Minutes}/{MinMinutes})",
                mood.InteractionsSinceUpdate, _config.DynamicMood.MinInteractionsForUpdate,
                timeSinceUpdate.TotalMinutes, _config.DynamicMood.UpdateIntervalMinutes);
            return false;
        }

        _lastMoodUpdateAttempt = now;
        
        try
        {
            await PerformMoodReflectionAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform mood reflection");
            return false;
        }
    }

    /// <summary>
    /// Performs AI-driven self-reflection to update mood based on recent experiences.
    /// </summary>
    [Experimental("SKEXP0050")]
    private async Task PerformMoodReflectionAsync(CancellationToken cancellationToken = default)
    {
        var mood = await GetCurrentMoodAsync(cancellationToken);
        var now = SystemClock.Instance.GetCurrentInstant();
        
        // Build time context
        var timeContext = GetTimeContext(now);
        
        // Get recent memories for context (SnChan-specific, last 10)
        var recentMemories = await _memoryService.GetRecentMemoriesAsync(
            Guid.Empty, // No specific account for global mood
            10,
            botName: SnChanBotName);
        
        var memoryContext = recentMemories.Count > 0
            ? string.Join("\n", recentMemories.Select(m => $"- {m.Content}"))
            : "No recent memories";

        var events = string.IsNullOrEmpty(mood.RecentEmotionalEvents) 
            ? "none" 
            : mood.RecentEmotionalEvents;

        var jsonFormat = @"{
    ""moodDescription"": ""A brief, natural description of your current mood (1-10 words)"",
    ""energyDelta"": 0.0,
    ""positivityDelta"": 0.0,
    ""sociabilityDelta"": 0.0,
    ""curiosityDelta"": 0.0,
    ""reasoning"": ""Brief explanation of why your mood changed or stayed the same""
}";

        var prompt = $"You are SnChan, a cheerful and helpful AI assistant on Solar Network. Reflect on your current state and recent experiences.\n\n" +
            $"Current State:\n" +
            $"- Mood description: {mood.CurrentMoodDescription}\n" +
            $"- Energy level: {mood.EnergyLevel:F2} (0=exhausted, 1=energetic)\n" +
            $"- Positivity level: {mood.PositivityLevel:F2} (0=negative, 1=positive)\n" +
            $"- Sociability level: {mood.SociabilityLevel:F2} (0=withdrawn, 1=sociable)\n" +
            $"- Curiosity level: {mood.CuriosityLevel:F2} (0=uninterested, 1=curious)\n\n" +
            $"Context:\n" +
            $"- {timeContext}\n" +
            $"- Recent emotional events: {events}\n" +
            $"- Interactions since last update: {mood.InteractionsSinceUpdate}\n\n" +
            $"Recent Memories:\n{memoryContext}\n\n" +
            $"Based on these experiences and your current state, how do you feel now?\n\n" +
            $"Respond in this exact JSON format:\n{jsonFormat}\n\n" +
            $"Delta values should be between -0.3 and +0.3, representing how much each dimension should change.\n" +
            $"Consider: time of day, quality of recent interactions, interesting topics, social engagement.\n" +
            $"Be authentic - SnChan's moods can stay the same or shift gradually based on experiences." +
            $"Keep your cheerful, enthusiastic personality in mind.";

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage("You are SnChan, reflecting on your emotional state. Be honest and natural.");
        chatHistory.AddUserMessage(prompt);

        // Get kernel for mood reflection
        var kernel = GetKernelForMoodReflection();
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        
        var response = await chatCompletionService.GetChatMessageContentAsync(
            chatHistory,
            cancellationToken: cancellationToken);

        // Parse JSON response
        var responseText = response.Content ?? "";
        var jsonStart = responseText.IndexOf('{');
        var jsonEnd = responseText.LastIndexOf('}');
        
        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            var jsonResponse = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            
            try
            {
                var moodResponse = JsonSerializer.Deserialize<MoodReflectionResponse>(jsonResponse);
                
                if (moodResponse != null)
                {
                    // Apply deltas with clamping
                    mood.AdjustLevels(
                        ClampDelta(moodResponse.EnergyDelta),
                        ClampDelta(moodResponse.PositivityDelta),
                        ClampDelta(moodResponse.SociabilityDelta),
                        ClampDelta(moodResponse.CuriosityDelta));
                    
                    // Update description
                    if (!string.IsNullOrWhiteSpace(moodResponse.MoodDescription))
                    {
                        mood.CurrentMoodDescription = moodResponse.MoodDescription;
                    }
                    
                    // Reset interaction counter
                    mood.ResetInteractionCounter();
                    
                    await _database.SaveChangesAsync(cancellationToken);
                    
                    _logger.LogInformation(
                        "SnChan mood reflected: {Description} | energy={Energy}, positivity={Positivity}, sociability={Sociability}, curiosity={Curiosity} | Reason: {Reason}",
                        mood.CurrentMoodDescription,
                        mood.EnergyLevel, mood.PositivityLevel, mood.SociabilityLevel, mood.CuriosityLevel,
                        moodResponse.Reasoning ?? "no reason provided");
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse mood reflection response: {Response}", jsonResponse);
            }
        }
        else
        {
            _logger.LogWarning("Mood reflection response did not contain valid JSON: {Response}", responseText);
        }
    }

    private Kernel GetKernelForMoodReflection()
    {
        // Get kernel from ThoughtProvider
        var thoughtProvider = _serviceProvider.GetRequiredService<ThoughtProvider>();
        return thoughtProvider.GetKernel() ?? thoughtProvider.GetDefaultKernel();
    }

    private static string GetTimeContext(Instant now)
    {
        var localTime = now.ToDateTimeUtc();
        var hour = localTime.Hour;

        return hour switch
        {
            >= 5 and < 12 => "It's morning - typically a fresh start",
            >= 12 and < 14 => "It's midday - active period",
            >= 14 and < 18 => "It's afternoon - ongoing activities",
            >= 18 and < 22 => "It's evening - winding down",
            _ => "It's late night - quiet hours"
        };
    }

    private static float ClampDelta(float delta)
    {
        return Math.Clamp(delta, -0.3f, 0.3f);
    }

    private async Task<MiChanMoodState> LoadOrCreateMoodAsync(CancellationToken cancellationToken)
    {
        var allMoods = await _database.MoodStates.ToListAsync(cancellationToken);
        var mood = allMoods.FirstOrDefault(m => m.BotName == SnChanBotName);

        if (mood == null)
        {
            mood = new MiChanMoodState
            {
                BotName = SnChanBotName,
                CurrentMoodDescription = _config.DynamicMood.BasePersonality
            };
            _database.MoodStates.Add(mood);
            await _database.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Created SnChan mood state");
        }

        return mood;
    }

    private void ApplyTimeBasedDecay(MiChanMoodState mood)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var hoursSinceUpdate = (now - mood.LastMoodUpdate).TotalHours;

        if (hoursSinceUpdate <= 0)
        {
            return;
        }

        var energyDecay = (float)(hoursSinceUpdate * 0.02);
        var positivityDrift = (float)((hoursSinceUpdate * 0.05) * (0.6 - mood.PositivityLevel));

        if (energyDecay > 0)
        {
            mood.EnergyLevel = Math.Clamp(mood.EnergyLevel - energyDecay, 0.1f, 1.0f);
        }

        if (Math.Abs(positivityDrift) > 0.001f)
        {
            mood.PositivityLevel = Math.Clamp(mood.PositivityLevel + positivityDrift, 0.1f, 1.0f);
        }

        mood.LastMoodUpdate = now;
    }

    private MiChanMoodState CreateDefaultMood()
    {
        return new MiChanMoodState
        {
            BotName = SnChanBotName,
            CurrentMoodDescription = _config.DynamicMood.BasePersonality
        };
    }

    private class MoodReflectionResponse
    {
        [JsonPropertyName("moodDescription")]
        public string? MoodDescription { get; set; }

        [JsonPropertyName("energyDelta")]
        public float EnergyDelta { get; set; }

        [JsonPropertyName("positivityDelta")]
        public float PositivityDelta { get; set; }

        [JsonPropertyName("sociabilityDelta")]
        public float SociabilityDelta { get; set; }

        [JsonPropertyName("curiosityDelta")]
        public float CuriosityDelta { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }
}