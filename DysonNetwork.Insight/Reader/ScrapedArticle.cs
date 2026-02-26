using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using EmbedLinkEmbed = DysonNetwork.Shared.Models.Embed.LinkEmbed;

namespace DysonNetwork.Insight.Reader;

public class ScrapedArticle
{
    public EmbedLinkEmbed LinkEmbed { get; set; } = null!;
    public string? Content { get; set; }

    public DyScrapedArticle ToProtoValue()
    {
        var proto = new DyScrapedArticle
        {
            LinkEmbed = LinkEmbed.ToProtoValue()
        };

        if (!string.IsNullOrEmpty(Content))
            proto.Content = Content;

        return proto;
    }

    public static ScrapedArticle FromProtoValue(DyScrapedArticle proto)
    {
        return new ScrapedArticle
        {
            LinkEmbed = EmbedLinkEmbed.FromProtoValue(proto.LinkEmbed),
            Content = proto.Content == "" ? null : proto.Content
        };
    }
}