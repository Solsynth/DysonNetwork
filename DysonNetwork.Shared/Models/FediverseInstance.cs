using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Domain), nameof(DeletedAt), IsUnique = true)]
public class SnFediverseInstance : ModelBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)]
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = null!;

    [MaxLength(512)]
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [MaxLength(4096)]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("software")]
    public string? Software { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("icon_url")]
    public string? IconUrl { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [MaxLength(512)]
    [JsonPropertyName("contact_email")]
    public string? ContactEmail { get; set; }

    [MaxLength(256)]
    [JsonPropertyName("contact_account_username")]
    public string? ContactAccountUsername { get; set; }

    [JsonPropertyName("active_users")]
    public int? ActiveUsers { get; set; }

    [Column(TypeName = "jsonb")]
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("is_blocked")]
    public bool IsBlocked { get; set; } = false;

    [JsonPropertyName("is_silenced")]
    public bool IsSilenced { get; set; } = false;

    [MaxLength(2048)]
    [JsonPropertyName("block_reason")]
    public string? BlockReason { get; set; }

    [JsonIgnore]
    public List<SnFediverseActor> Actors { get; set; } = [];

    [JsonPropertyName("last_fetched_at")]
    public Instant? LastFetchedAt { get; set; }

    [JsonPropertyName("last_activity_at")]
    public Instant? LastActivityAt { get; set; }

    [JsonPropertyName("metadata_fetched_at")]
    public Instant? MetadataFetchedAt { get; set; }
}
