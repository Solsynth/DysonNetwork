using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnFediverseReaction : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [MaxLength(2048)]
    public string Uri { get; set; } = null!;
    
    public FediverseReactionType Type { get; set; }
    
    [MaxLength(64)]
    public string? Emoji { get; set; }
    
    public bool IsLocal { get; set; }
    
    public Guid ContentId { get; set; }
    [JsonIgnore]
    public SnFediverseContent Content { get; set; } = null!;
    
    public Guid ActorId { get; set; }
    [JsonIgnore]
    public SnFediverseActor Actor { get; set; } = null!;
    
    public Guid? LocalAccountId { get; set; }
    public Guid? LocalReactionId { get; set; }
}

public enum FediverseReactionType
{
    Like,
    Emoji,
    Dislike
}
