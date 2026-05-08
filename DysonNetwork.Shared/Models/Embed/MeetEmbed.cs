namespace DysonNetwork.Shared.Models.Embed;

/// <summary>
/// The meet embed can be used in the post or messages' meta embeds field.
/// </summary>
public class MeetEmbed : EmbeddableBase
{
    public override string Type => "meet";

    public required Guid Id { get; set; }
}
