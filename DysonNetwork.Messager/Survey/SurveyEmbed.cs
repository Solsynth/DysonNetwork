using DysonNetwork.Shared.Models.Embed;

namespace DysonNetwork.Messager.Survey;

public class SurveyEmbed : EmbeddableBase
{
    public override string Type => "survey";

    public Guid Id { get; set; }
}
