using DysonNetwork.Shared.Proto;
using EmbedLinkEmbed = DysonNetwork.Shared.Models.Embed.LinkEmbed;

namespace DysonNetwork.Insight.Reader;

public record ScrapedArticle
{
    public required EmbedLinkEmbed LinkEmbed { get; init; }
    public string? Content { get; init; }

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