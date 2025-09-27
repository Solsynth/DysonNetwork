using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.Models;

public class SnDeveloper
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PublisherId { get; set; }
    
    [JsonIgnore] public List<SnDevProject> Projects { get; set; } = [];
    
    [NotMapped] public SnPublisher? Publisher { get; set; }
}
