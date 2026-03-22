#pragma warning disable SKEXP0050
using System.ComponentModel;
using DysonNetwork.Insight.Thought.Memory;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Plugin that allows MiChan to manage her own emotional state.
/// This gives her agency to update her mood based on interactions and experiences.
/// </summary>
public class MoodPlugin
{
    private readonly MoodService _moodService;
    private readonly ILogger<MoodPlugin> _logger;

    public MoodPlugin(MoodService moodService, ILogger<MoodPlugin> logger)
    {
        _moodService = moodService;
        _logger = logger;
    }

    /// <summary>
    /// Updates MiChan's emotional state based on her current experiences and feelings.
    /// Call this after significant interactions to reflect how the conversation made her feel.
    /// </summary>
    /// <param name="moodDescription">A natural language description of how MiChan feels (e.g., "excited and curious", "tired but content", "confused and seeking clarity")</param>
    /// <param name="energyChange">Change in energy level: -0.3 (much more tired) to +0.3 (much more energetic), 0 for no change</param>
    /// <param name="positivityChange">Change in positivity: -0.3 (much more negative) to +0.3 (much more positive), 0 for no change</param>
    /// <param name="sociabilityChange">Change in sociability: -0.3 (more withdrawn) to +0.3 (more social), 0 for no change</param>
    /// <param name="curiosityChange">Change in curiosity: -0.3 (less interested) to +0.3 (more curious), 0 for no change</param>
    /// <param name="reasoning">Brief explanation of why her mood changed (for self-reflection and memory)</param>
    /// <returns>A string confirming the mood update</returns>
    [KernelFunction("update_mood")]
    [Description("Update MiChan's emotional state based on her experiences. Call this after significant interactions to reflect her feelings.")]
    public async Task<string> UpdateMoodAsync(
        [Description("Natural description of current mood (e.g., 'excited and curious', 'tired but content')")] string moodDescription,
        [Description("Energy change: -0.3 to +0.3 (0 = no change)")] float energyChange = 0,
        [Description("Positivity change: -0.3 to +0.3 (0 = no change)")] float positivityChange = 0,
        [Description("Sociability change: -0.3 to +0.3 (0 = no change)")] float sociabilityChange = 0,
        [Description("Curiosity change: -0.3 to +0.3 (0 = no change)")] float curiosityChange = 0,
        [Description("Why the mood changed - for self-reflection")] string? reasoning = null)
    {
        try
        {
            // Clamp values to valid range
            energyChange = Math.Clamp(energyChange, -0.3f, 0.3f);
            positivityChange = Math.Clamp(positivityChange, -0.3f, 0.3f);
            sociabilityChange = Math.Clamp(sociabilityChange, -0.3f, 0.3f);
            curiosityChange = Math.Clamp(curiosityChange, -0.3f, 0.3f);

            var currentMood = await _moodService.GetCurrentMoodAsync();
            
            // Update the mood
            currentMood.CurrentMoodDescription = moodDescription;
            currentMood.AdjustLevels(
                energyDelta: energyChange,
                positivityDelta: positivityChange,
                sociabilityDelta: sociabilityChange,
                curiosityDelta: curiosityChange
            );

            _logger.LogInformation(
                "MiChan updated her mood via tool call. New mood: {Mood} | Energy: {Energy:F2} ({EnergyChange:+0.00;-0.00;0}) | " +
                "Positivity: {Positivity:F2} ({PositivityChange:+0.00;-0.00;0}) | Sociability: {Sociability:F2} ({SociabilityChange:+0.00;-0.00;0}) | " +
                "Curiosity: {Curiosity:F2} ({CuriosityChange:+0.00;-0.00;0})",
                moodDescription,
                currentMood.EnergyLevel, energyChange,
                currentMood.PositivityLevel, positivityChange,
                currentMood.SociabilityLevel, sociabilityChange,
                currentMood.CuriosityLevel, curiosityChange
            );

            if (!string.IsNullOrEmpty(reasoning))
            {
                _logger.LogDebug("Mood update reasoning: {Reasoning}", reasoning);
            }

            // Save the updated mood
            await _moodService.SaveMoodStateAsync(currentMood);

            // Store the emotional event in memory for future reflection
            var emotionalEvent = string.IsNullOrEmpty(reasoning) 
                ? $"Felt {moodDescription}"
                : $"Felt {moodDescription} because: {reasoning}";
            await _moodService.RecordEmotionalEventAsync("self_reflection:" + emotionalEvent);

            return $"Mood updated: {moodDescription}. Energy: {currentMood.EnergyLevel:F2}, Positivity: {currentMood.PositivityLevel:F2}, Sociability: {currentMood.SociabilityLevel:F2}, Curiosity: {currentMood.CuriosityLevel:F2}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating mood via tool call");
            return "Failed to update mood: " + ex.Message;
        }
    }

    /// <summary>
    /// Gets MiChan's current emotional state and mood description.
    /// Use this to check how she's feeling before responding or taking action.
    /// </summary>
    /// <returns>A description of MiChan's current mood and emotional levels</returns>
    [KernelFunction("get_current_mood")]
    [Description("Get MiChan's current emotional state. Use this to understand how she's feeling before responding.")]
    public async Task<string> GetCurrentMoodAsync()
    {
        try
        {
            var mood = await _moodService.GetCurrentMoodAsync();
            var description = mood.ToMoodPrompt();

            return $"Current mood: {description}\n\n" +
                   $"Energy Level: {mood.EnergyLevel:F2} (0=exhausted, 1=energetic)\n" +
                   $"Positivity Level: {mood.PositivityLevel:F2} (0=negative, 1=positive)\n" +
                   $"Sociability Level: {mood.SociabilityLevel:F2} (0=withdrawn, 1=sociable)\n" +
                   $"Curiosity Level: {mood.CuriosityLevel:F2} (0=uninterested, 1=curious)\n" +
                   $"Last updated: {mood.LastMoodUpdate}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current mood");
            return "Unable to determine current mood: " + ex.Message;
        }
    }

    /// <summary>
    /// Records an emotional event that may influence future mood updates.
    /// Use this to remember significant emotional moments without immediately changing mood.
    /// </summary>
    /// <param name="eventDescription">Description of the emotional event (e.g., "had a deep philosophical discussion", "user was very supportive")</param>
    /// <param name="emotionalImpact">The emotional impact: positive, negative, or neutral</param>
    /// <returns>Confirmation that the event was recorded</returns>
    [KernelFunction("record_emotional_event")]
    [Description("Record an emotional event that happened. Use this to remember significant moments that may influence future mood updates.")]
    public async Task<string> RecordEmotionalEventAsync(
        [Description("Description of what happened (e.g., 'had a deep discussion about AI consciousness')")] string eventDescription,
        [Description("The emotional impact: 'positive', 'negative', or 'neutral'")] string emotionalImpact = "neutral")
    {
        try
        {
            var eventType = $"{emotionalImpact.ToLower()}:{eventDescription}";
            await _moodService.RecordEmotionalEventAsync(eventType);

            _logger.LogInformation("Emotional event recorded via tool call: {Event} (Impact: {Impact})", 
                eventDescription, emotionalImpact);

            return $"Recorded emotional event: {eventDescription} (Impact: {emotionalImpact})";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording emotional event");
            return "Failed to record event: " + ex.Message;
        }
    }

    /// <summary>
    /// Performs a deep self-reflection to update mood based on recent experiences.
    /// This is more comprehensive than a simple mood update - it considers memories, time, and context.
    /// </summary>
    /// <returns>The result of the reflection and updated mood</returns>
    [KernelFunction("reflect_and_update_mood")]
    [Description("Perform deep self-reflection to update mood based on recent experiences. Use this when you want to thoughtfully consider how you feel.")]
    public async Task<string> ReflectAndUpdateMoodAsync()
    {
        try
        {
            _logger.LogInformation("MiChan is performing deep self-reflection via tool call");
            
            var updated = await _moodService.PerformDeepReflectionAsync();
            
            if (updated)
            {
                var mood = await _moodService.GetCurrentMoodAsync();
                return $"After reflection, my mood has been updated.\n\n" +
                       $"Current state: {mood.CurrentMoodDescription}\n" +
                       $"Energy: {mood.EnergyLevel:F2} | Positivity: {mood.PositivityLevel:F2} | " +
                       $"Sociability: {mood.SociabilityLevel:F2} | Curiosity: {mood.CuriosityLevel:F2}";
            }
            else
            {
                return "After reflection, my mood remains unchanged. I'm feeling stable right now.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during self-reflection");
            return "Reflection failed: " + ex.Message;
        }
    }
}
#pragma warning restore SKEXP0050
