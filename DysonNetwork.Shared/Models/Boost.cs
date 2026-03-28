using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnBoost : ModelBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("post_id")]
    public Guid PostId { get; set; }

    [JsonIgnore]
    public SnPost Post { get; set; } = null!;

    [JsonPropertyName("actor_id")]
    public Guid ActorId { get; set; }

    [JsonIgnore]
    public SnFediverseActor Actor { get; set; } = null!;

    [MaxLength(2048)]
    [JsonPropertyName("activity_pub_uri")]
    public string? ActivityPubUri { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("web_url")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("boosted_at")]
    public Instant BoostedAt { get; set; }
}
