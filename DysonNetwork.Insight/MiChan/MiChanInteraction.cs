using System.Text.Json;
using DysonNetwork.Shared.Data;
using NodaTime;

namespace DysonNetwork.Insight.MiChan;

/// <summary>
/// In-memory model for MiChan interactions.
/// This is used for caching and service-layer operations, not for database storage.
/// Persistent storage is handled by AgentVectorService using AgentMemoryRecord.
/// </summary>
public class MiChanInteraction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = null!;
    public string ContextId { get; set; } = null!;
    public Dictionary<string, object> Context { get; set; } = new();
    public Dictionary<string, object> Memory { get; set; } = new();
    public string? EmbeddedContent { get; set; }
    public Instant CreatedAt { get; set; }
}

public static class MiChanInteractionExtensions
{
    public static T? GetMemoryValue<T>(this MiChanInteraction interaction, string key, T? defaultValue = default)
    {
        if (!interaction.Memory.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (value == null)
        {
            return defaultValue;
        }

        if (value is T typed)
        {
            return typed;
        }

        if (value is JsonElement element)
        {
            try
            {
                return element.Deserialize<T>(InfraObjectCoder.SerializerOptions);
            }
            catch
            {
                return defaultValue;
            }
        }

        try
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value), InfraObjectCoder.SerializerOptions);
        }
        catch
        {
            return defaultValue;
        }
    }

    public static T? GetContextValue<T>(this MiChanInteraction interaction, string key, T? defaultValue = default)
    {
        if (!interaction.Context.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (value == null)
        {
            return defaultValue;
        }

        if (value is T typed)
        {
            return typed;
        }

        if (value is JsonElement element)
        {
            try
            {
                return element.Deserialize<T>(InfraObjectCoder.SerializerOptions);
            }
            catch
            {
                return defaultValue;
            }
        }

        try
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value), InfraObjectCoder.SerializerOptions);
        }
        catch
        {
            return defaultValue;
        }
    }
}
