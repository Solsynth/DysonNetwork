using DysonNetwork.Sphere.WebReader;

namespace DysonNetwork.Sphere.Wallet;

public class FundEmbed : EmbeddableBase
{
    public override string Type => "fund";
    
    public Guid Id { get; set; }
}
