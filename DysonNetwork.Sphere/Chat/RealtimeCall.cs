using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Chat.Realtime;
using NodaTime;

namespace DysonNetwork.Sphere.Chat;

public class RealtimeCall : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Instant? EndedAt { get; set; }

    public Guid SenderId { get; set; }
    public ChatMember Sender { get; set; } = null!;
    public Guid RoomId { get; set; }
    public ChatRoom Room { get; set; } = null!;

    /// <summary>
    /// Provider name (e.g., "cloudflare", "agora", "twilio")
    /// </summary>
    public string? ProviderName { get; set; }

    /// <summary>
    /// Service provider's session identifier
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// JSONB column containing provider-specific configuration
    /// </summary>
    [Column(name: "upstream", TypeName = "jsonb")]
    public string? UpstreamConfigJson { get; set; }

    /// <summary>
    /// Deserialized upstream configuration
    /// </summary>
    [NotMapped]
    public Dictionary<string, object> UpstreamConfig
    {
        get => string.IsNullOrEmpty(UpstreamConfigJson)
            ? new Dictionary<string, object>()
            : JsonSerializer.Deserialize<Dictionary<string, object>>(UpstreamConfigJson) ?? new Dictionary<string, object>();
        set => UpstreamConfigJson = value.Count > 0 
            ? JsonSerializer.Serialize(value) 
            : null;
    }
}