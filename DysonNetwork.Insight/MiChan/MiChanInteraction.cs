using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using NodaTime;
using Pgvector;

namespace DysonNetwork.Insight.MiChan;

public class MiChanInteraction : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = null!;
    public string ContextId { get; set; } = null!;
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Context { get; set; } = new();
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Memory { get; set; } = new();
    
    [Column(TypeName = "vector(1536)")]
    public Vector? Embedding { get; set; }
    
    public string? EmbeddedContent { get; set; }
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
