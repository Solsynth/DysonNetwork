using DysonNetwork.Sphere.WebReader;

namespace DysonNetwork.Sphere.Poll;

public class PollWithStats : Poll
{
    public PollAnswer? UserAnswer { get; set; }
    public Dictionary<Guid, Dictionary<string, int>> Stats { get; set; } = new(); // question id -> (option id -> count)

    public static PollWithStats FromPoll(Poll poll, PollAnswer? userAnswer = null)
    {
        return new PollWithStats
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

public class PollEmbed : EmbeddableBase
{
    public override string Type => "poll";
    
    public required Guid Id { get; set; }
    
    /// <summary>
    /// Do not store this to the database
    /// Only set this when sending the embed
    /// </summary>
    public PollWithStats? Poll { get; set; }
}