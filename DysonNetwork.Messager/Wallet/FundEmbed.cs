using DysonNetwork.Shared.Models.Embed;

namespace DysonNetwork.Messager.Wallet;

public class FundEmbed : EmbeddableBase
{
    public override string Type => "fund";

    public Guid Id { get; set; }
}
