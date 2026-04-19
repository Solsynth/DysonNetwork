using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using NodaTime;
using NodaTime.Extensions;

#pragma warning disable SKEXP0050
namespace DysonNetwork.Insight.SnChan;

public class SnChanMoodService
{
    private readonly AppDatabase _database;
    private readonly ILogger<SnChanMoodService> _logger;
    private readonly SnChanConfig _config;
    private readonly Lock _moodLock = new();
    private const string SnChanBotName = "snchan";

    private MiChanMoodState? _cachedMood;
    private Instant _lastCacheTime;
    private static readonly Duration CacheDuration = Duration.FromMinutes(5);

    public SnChanMoodService(
        AppDatabase database,
        ILogger<SnChanMoodService> logger,
        SnChanConfig config)
    {
        _database = database;
        _logger = logger;
        _config = config;
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
}