using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;

namespace DysonNetwork.Sphere.Poll;

public class Poll : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<PollQuestion> Questions { get; set; } = new();
    
    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }
    
    public Instant? EndedAt { get; set; }
    
    public Guid PublisherId { get; set; }
    public Publisher.Publisher Publisher { get; set; } = null!;
}

public class PollWithAnswer : Poll
{
    public PollAnswer? UserAnswer { get; set; }
    public Dictionary<Guid, Dictionary<string, int>> Stats { get; set; } = new(); // question id -> (option id -> count)

    public static PollWithAnswer FromPoll(Poll poll, PollAnswer? userAnswer = null)
    {
        return new PollWithAnswer
        {
            Id = poll.Id,
            Title = poll.Title,
            Description = poll.Description,
            EndedAt = poll.EndedAt,
            PublisherId = poll.PublisherId,
            Publisher = poll.Publisher,
            Questions = poll.Questions,
            CreatedAt = poll.CreatedAt,
            UpdatedAt = poll.UpdatedAt,
            UserAnswer = userAnswer
        };
    }
}

public enum PollQuestionType
{
    SingleChoice,
    MultipleChoice,
    YesNo,
    Rating,
    FreeText
}

public class PollQuestion : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    public PollQuestionType Type { get; set; }
    [Column(TypeName = "jsonb")] public List<PollOption>? Options { get; set; }
    
    [MaxLength(1024)] public string Title { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public int Order { get; set; } = 0;
    public bool IsRequired { get; set; }
    
    public Guid PollId { get; set; }
    [JsonIgnore] public Poll Poll { get; set; } = null!;
}

public class PollOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required] [MaxLength(1024)] public string Label { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public int Order { get; set; } = 0;
}

public class PollAnswer : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Column(TypeName = "jsonb")] public Dictionary<string, JsonElement> Answer { get; set; } = null!;
    
    public Guid AccountId { get; set; }
    public Guid PollId { get; set; }
    [JsonIgnore] public Poll Poll { get; set; } = null!;
}