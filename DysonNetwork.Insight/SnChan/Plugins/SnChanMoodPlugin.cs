using System.ComponentModel;
using System.Text.Json;
using DysonNetwork.Insight.MiChan;
using DysonNetwork.Insight.Thought.Memory;
using DysonNetwork.Insight.Agent.Foundation;

namespace DysonNetwork.Insight.SnChan.Plugins;

public class SnChanMoodPlugin(
    SnChanMoodService moodService,
    ILogger<SnChanMoodPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private const string BotName = "snchan";

    [AgentTool("get_current_mood", Description = "Get SnChan's current mood state as a description")]
    public async Task<string> GetCurrentMood(
        [AgentToolParameter("Include detailed metrics (energy, positivity, sociability, curiosity levels)")]
        bool includeMetrics = false
    )
    {
        var mood = await moodService.GetCurrentMoodAsync();
        
        if (includeMetrics)
        {
            return JsonSerializer.Serialize(new 
            { 
                description = mood.ToMoodPrompt(),
                energy = mood.EnergyLevel,
                positivity = mood.PositivityLevel,
                sociability = mood.SociabilityLevel,
                curiosity = mood.CuriosityLevel,
                recentEvents = mood.RecentEmotionalEvents
            }, JsonOptions);
        }
        
        return mood.ToMoodPrompt();
    }

    [AgentTool("update_mood", Description = "Update SnChan's mood by adjusting levels. Use small deltas (-0.3 to +0.3) for each mood dimension.")]
    public async Task<string> UpdateMood(
        [AgentToolParameter("Energy delta: -0.3 (tired) to +0.3 (energetic)")]
        float? energyDelta = null,
        [AgentToolParameter("Positivity delta: -0.3 (negative) to +0.3 (positive)")]
        float? positivityDelta = null,
        [AgentToolParameter("Sociability delta: -0.3 (withdrawn) to +0.3 (social)")]
        float? sociabilityDelta = null,
        [AgentToolParameter("Curiosity delta: -0.3 (indifferent) to +0.3 (curious)")]
        float? curiosityDelta = null
    )
    {
        var clampedEnergy = ClampDelta(energyDelta);
        var clampedPositivity = ClampDelta(positivityDelta);
        var clampedSociability = ClampDelta(sociabilityDelta);
        var clampedCuriosity = ClampDelta(curiosityDelta);

        if (!clampedEnergy.HasValue && !clampedPositivity.HasValue && !clampedSociability.HasValue && !clampedCuriosity.HasValue)
        {
            return JsonSerializer.Serialize(new 
            { 
                success = false, 
                error = "At least one delta must be provided" 
            }, JsonOptions);
        }

        await moodService.UpdateMoodAsync(
            clampedEnergy,
            clampedPositivity,
            clampedSociability,
            clampedCuriosity);

        var mood = await moodService.GetCurrentMoodAsync();
        return JsonSerializer.Serialize(new 
        { 
            success = true,
            message = "Mood updated",
            currentMood = mood.ToMoodPrompt()
        }, JsonOptions);
    }

    [AgentTool("record_emotional_event", Description = "Record an emotional event that may influence future mood. Events are recorded for AI self-reflection.")]
    public async Task<string> RecordEmotionalEvent(
        [AgentToolParameter("The event type: 'positive_interaction', 'negative_interaction', 'mentioned', 'replied', 'helped_user', 'confused', etc.")]
        string eventType
    )
    {
        await moodService.RecordInteractionAsync(eventType);
        
        return JsonSerializer.Serialize(new 
        { 
            success = true, 
            message = $"Event '{eventType}' recorded" 
        }, JsonOptions);
    }

    [AgentTool("reflect_and_update_mood", Description = "Trigger AI self-reflection to update SnChan's mood based on recent experiences. Use this when you want to reflect on your emotional state.")]
    public async Task<string> ReflectAndUpdateMood()
    {
        var success = await moodService.TryUpdateMoodAsync();
        
        if (success)
        {
            var mood = await moodService.GetCurrentMoodAsync();
            return JsonSerializer.Serialize(new 
            { 
                success = true, 
                message = "Mood reflection completed",
                currentMood = mood.ToMoodPrompt()
            }, JsonOptions);
        }
        
        return JsonSerializer.Serialize(new 
        { 
            success = false, 
            message = "Mood reflection skipped (cooldown active or insufficient interactions)" 
        }, JsonOptions);
    }

    private float? ClampDelta(float? delta)
    {
        if (!delta.HasValue) return null;
        return Math.Clamp(delta.Value, -0.3f, 0.3f);
    }
}
