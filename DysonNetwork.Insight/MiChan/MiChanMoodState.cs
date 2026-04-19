using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// Represents MiChan's current emotional and psychological state.
/// Multi-dimensional mood tracking allows for dynamic personality expression.
/// </summary>
[Table("mood_states")]
public class MiChanMoodState : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Bot name to isolate mood states per bot (e.g., "michan", "snchan")
    /// If null, the record is for MiChan (backward compatible)
    /// </summary>
    public string? BotName { get; set; }

    /// <summary>
    /// Energy level: 0.0 (exhausted) to 1.0 (bursting with energy)
    /// Affects activity level and enthusiasm
    /// </summary>
    public float EnergyLevel { get; set; } = 0.7f;

    /// <summary>
    /// Positivity level: 0.0 (negative/pessimistic) to 1.0 (optimistic/happy)
    /// Affects tone and interpretation of interactions
    /// </summary>
    public float PositivityLevel { get; set; } = 0.7f;

    /// <summary>
    /// Sociability level: 0.0 (withdrawn) to 1.0 (gregarious)
    /// Affects desire to interact and initiate conversations
    /// </summary>
    public float SociabilityLevel { get; set; } = 0.6f;

    /// <summary>
    /// Curiosity level: 0.0 (disinterested) to 1.0 (highly curious)
    /// Affects engagement depth and philosophical tendencies
    /// </summary>
    public float CuriosityLevel { get; set; } = 0.8f;

    /// <summary>
    /// Free-text description of current mood (e.g., "reflective and slightly melancholic")
    /// Generated dynamically by AI self-reflection
    /// </summary>
    [Column(TypeName = "text")]
    public string CurrentMoodDescription { get; set; } = "curious and friendly";

    /// <summary>
    /// Recent emotional events that influenced the current mood
    /// Stored as comma-separated tags for quick reference
    /// </summary>
    public string? RecentEmotionalEvents { get; set; }

    /// <summary>
    /// When the mood was last updated
    /// </summary>
    public Instant LastMoodUpdate { get; set; } = SystemClock.Instance.GetCurrentInstant();

    /// <summary>
    /// Number of interactions since last mood update
    /// Used to determine when to trigger mood reflection
    /// </summary>
    public int InteractionsSinceUpdate { get; set; } = 0;

    /// <summary>
    /// Gets a clamped value between 0.0 and 1.0
    /// </summary>
    private static float Clamp(float value) => Math.Clamp(value, 0.0f, 1.0f);

    /// <summary>
    /// Updates mood levels ensuring they stay within bounds
    /// </summary>
    public void UpdateLevels(float? energy = null, float? positivity = null, float? sociability = null, float? curiosity = null)
    {
        if (energy.HasValue) EnergyLevel = Clamp(energy.Value);
        if (positivity.HasValue) PositivityLevel = Clamp(positivity.Value);
        if (sociability.HasValue) SociabilityLevel = Clamp(sociability.Value);
        if (curiosity.HasValue) CuriosityLevel = Clamp(curiosity.Value);
        LastMoodUpdate = SystemClock.Instance.GetCurrentInstant();
    }

    /// <summary>
    /// Adjusts mood levels by the given deltas (can be negative)
    /// </summary>
    public void AdjustLevels(float energyDelta = 0, float positivityDelta = 0, float sociabilityDelta = 0, float curiosityDelta = 0)
    {
        EnergyLevel = Clamp(EnergyLevel + energyDelta);
        PositivityLevel = Clamp(PositivityLevel + positivityDelta);
        SociabilityLevel = Clamp(SociabilityLevel + sociabilityDelta);
        CuriosityLevel = Clamp(CuriosityLevel + curiosityDelta);
        LastMoodUpdate = SystemClock.Instance.GetCurrentInstant();
    }

    /// <summary>
    /// Converts the numerical mood state to a natural language description
    /// This is used for AI prompts
    /// </summary>
    public string ToMoodPrompt()
    {
        var parts = new List<string>();

        // Energy descriptor
        parts.Add(EnergyLevel switch
        {
            > 0.8f => " bursting with energy",
            > 0.6f => " energetic",
            > 0.4f => "",
            > 0.2f => " a bit tired",
            _ => " exhausted"
        });

        // Positivity descriptor
        parts.Add(PositivityLevel switch
        {
            > 0.8f => " very optimistic",
            > 0.6f => " positive",
            > 0.4f => "",
            > 0.2f => " somewhat down",
            _ => " feeling negative"
        });

        // Sociability descriptor
        parts.Add(SociabilityLevel switch
        {
            > 0.8f => " very social",
            > 0.6f => " sociable",
            > 0.4f => "",
            > 0.2f => " withdrawn",
            _ => " antisocial"
        });

        // Curiosity descriptor
        parts.Add(CuriosityLevel switch
        {
            > 0.8f => " highly curious",
            > 0.6f => " curious",
            > 0.4f => "",
            > 0.2f => " indifferent",
            _ => " uninterested"
        });

        var baseMood = string.Join(",", parts.Where(p => !string.IsNullOrWhiteSpace(p))).TrimStart(',');
        
        if (string.IsNullOrWhiteSpace(baseMood))
            baseMood = "neutral";

        return $"{CurrentMoodDescription}, currently feeling {baseMood}".Trim();
    }

    /// <summary>
    /// Records an interaction event that may affect mood
    /// </summary>
    public void RecordInteraction(string eventType)
    {
        InteractionsSinceUpdate++;
        
        var events = string.IsNullOrEmpty(RecentEmotionalEvents) 
            ? new List<string>() 
            : RecentEmotionalEvents.Split(',').ToList();
        
        events.Add(eventType);
        
        // Keep only last 10 events
        if (events.Count > 10)
            events = events.Skip(events.Count - 10).ToList();
        
        RecentEmotionalEvents = string.Join(",", events);
    }

    /// <summary>
    /// Clears interaction counter after mood update
    /// </summary>
    public void ResetInteractionCounter()
    {
        InteractionsSinceUpdate = 0;
    }
}
