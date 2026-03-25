using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnBoost : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PostId { get; set; }
    [JsonIgnore] public SnPost Post { get; set; } = null!;

    public Guid ActorId { get; set; }
    [JsonIgnore] public SnFediverseActor Actor { get; set; } = null!;

    [MaxLength(2048)]
    public string? ActivityPubUri { get; set; }

    [MaxLength(2048)]
    public string? WebUrl { get; set; }

    public string? Content { get; set; }

    public Instant BoostedAt { get; set; }
}
