using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Sphere.Models;

namespace DysonNetwork.Sphere.Live;

public class LiveStreamEmbed : EmbeddableBase
{
    public override string Type => "livestream";
    
    public Guid Id { get; set; }
}
