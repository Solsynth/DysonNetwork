using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnTicket : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(256)] public string Title { get; set; } = null!;

    public TicketType Type { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    public Guid CreatorId { get; set; }
    public SnAccount Creator { get; set; } = null!;
    
    public Guid? AssigneeId { get; set; }
    public SnAccount? Assignee { get; set; }
    public Instant? ResolvedAt { get; set; }

    public List<SnTicketMessage> Messages { get; set; } = [];
    [Column(TypeName = "jsonb")] public Dictionary<string, object> Metadata { get; set; } = [];
}

public class SnTicketMessage : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    [JsonIgnore] public SnTicket Ticket { get; set; } = null!;
    public Guid SenderId { get; set; }
    public SnAccount Sender { get; set; } = null!;
    [MaxLength(16384)] public string Content { get; set; } = null!;
    [Column(TypeName = "jsonb")] public List<SnCloudFileReferenceObject> Files { get; set; } = [];
}
