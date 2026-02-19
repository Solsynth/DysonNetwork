using DysonNetwork.Shared.Models.Embed;

namespace DysonNetwork.Sphere.Live;

public class LiveStreamEmbed : EmbeddableBase
{
    public override string Type => "livestream";
    
    public Guid Id { get; set; }
}
