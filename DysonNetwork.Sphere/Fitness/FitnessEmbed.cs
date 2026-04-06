using DysonNetwork.Shared.Models.Embed;

namespace DysonNetwork.Sphere.Fitness;

public class FitnessEmbed : EmbeddableBase
{
    public override string Type => "fitness";
    
    public required Guid Id { get; set; }
    
    public required string FitnessType { get; set; }
}
