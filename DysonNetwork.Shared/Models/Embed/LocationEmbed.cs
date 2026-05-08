namespace DysonNetwork.Shared.Models.Embed;

/// <summary>
/// The location embed can be used in the post or messages' meta embeds field.
/// </summary>
public class LocationEmbed : EmbeddableBase
{
    public override string Type => "location";

    public string? Name { get; set; }

    public string? Address { get; set; }

    public string? Wkt { get; set; }
}
