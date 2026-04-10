using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(KeyId), IsUnique = true)]
public class SnFediverseKey : ModelBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(512)]
    [JsonPropertyName("key_id")]
    public string KeyId { get; set; } = null!;

    [Column(TypeName = "TEXT")]
    [JsonPropertyName("key_pem")]
    public string KeyPem { get; set; } = null!;

    [Column(TypeName = "TEXT")]
    [JsonPropertyName("private_key_pem")]
    public string? PrivateKeyPem { get; set; }

    [JsonPropertyName("publisher_id")]
    public Guid? PublisherId { get; set; }

    [JsonPropertyName("actor_id")]
    public Guid? ActorId { get; set; }

    [JsonPropertyName("actor")]
    [ForeignKey(nameof(ActorId))]
    public SnFediverseActor? Actor { get; set; }

    [JsonPropertyName("created_at")]
    public Instant CreatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();

    [JsonPropertyName("rotated_at")]
    public Instant? RotatedAt { get; set; }

    [NotMapped]
    [JsonPropertyName("is_local")]
    public bool IsLocal => PublisherId.HasValue;
}