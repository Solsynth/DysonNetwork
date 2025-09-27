using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnPoll : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<SnPollQuestion> Questions { get; set; } = new();

    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }

    public Instant? EndedAt { get; set; }
    public bool IsAnonymous { get; set; }

    public Guid PublisherId { get; set; }
    [JsonIgnore] public SnPublisher? Publisher { get; set; }
}

public enum PollQuestionType
{
    SingleChoice,
    MultipleChoice,
    YesNo,
    Rating,
    FreeText
}

public class SnPollQuestion : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public PollQuestionType Type { get; set; }
    [Column(TypeName = "jsonb")] public List<SnPollOption>? Options { get; set; }

    [MaxLength(1024)] public string Title { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public int Order { get; set; } = 0;
    public bool IsRequired { get; set; }

    public Guid PollId { get; set; }
    [JsonIgnore] public SnPoll Poll { get; set; } = null!;
}

public class SnPollOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required][MaxLength(1024)] public string Label { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public int Order { get; set; } = 0;
}

public class SnPollAnswer : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Column(TypeName = "jsonb")] public Dictionary<string, JsonElement> Answer { get; set; } = null!;

    public Guid AccountId { get; set; }
    public Guid PollId { get; set; }
    [JsonIgnore] public SnPoll? Poll { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }
}
