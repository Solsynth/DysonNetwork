using DysonNetwork.Shared.Models.Embed;

namespace DysonNetwork.Messager.Poll;

public class PollEmbed : EmbeddableBase
{
    public override string Type => "poll";

    public Guid Id { get; set; }
}
