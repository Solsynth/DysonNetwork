using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;


namespace DysonNetwork.Sphere.Survey;

public class SurveyWithStats : SnSurvey
{
    public SnSurveyAnswer? UserAnswer { get; set; }
    public Dictionary<Guid, Dictionary<string, int>> Stats { get; set; } = new(); // question id -> (option id -> count)

    public static SurveyWithStats FromSurvey(SnSurvey survey, SnSurveyAnswer? userAnswer = null)
    {
        return new SurveyWithStats
        {
            Id = survey.Id,
            Title = survey.Title,
            Description = survey.Description,
            EndedAt = survey.EndedAt,
            Status = survey.Status,
            PublishedAt = survey.PublishedAt,
            NotifySubscribers = survey.NotifySubscribers,
            HideResults = survey.HideResults,
            Attachments = survey.Attachments,
            PublisherId = survey.PublisherId,
            Publisher = survey.Publisher,
            Questions = survey.Questions,
            CreatedAt = survey.CreatedAt,
            UpdatedAt = survey.UpdatedAt,
            UserAnswer = userAnswer
        };
    }
}

public class SurveyEmbed : EmbeddableBase
{
    public override string Type => "survey";

    public required Guid Id { get; set; }
}
