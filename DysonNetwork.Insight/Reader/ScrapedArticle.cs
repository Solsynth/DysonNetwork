using DysonNetwork.Shared.Models.Embed;

namespace DysonNetwork.Insight.Reader;

public class ScrapedArticle
{
    public LinkEmbed LinkEmbed { get; set; } = null!;
    public string? Content { get; set; }
}