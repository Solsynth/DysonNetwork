using System.ComponentModel.DataAnnotations.Schema;

namespace DysonNetwork.Insight.MiChan;

public class MiChanInteraction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = null!; // 'chat', 'autonomous', 'mention_response', 'admin'
    public string ContextId { get; set; } = null!; // Chat room ID or autonomous session ID
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Context { get; set; } = new();
    
    [Column(TypeName = "jsonb")]
    public Dictionary<string, object> Memory { get; set; } = new();
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
