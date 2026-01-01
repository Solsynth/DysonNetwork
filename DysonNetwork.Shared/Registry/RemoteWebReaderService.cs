using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using ProtoLinkEmbed = DysonNetwork.Shared.Proto.LinkEmbed;
using ModelsLinkEmbed = DysonNetwork.Shared.Models.Embed.LinkEmbed;

namespace DysonNetwork.Shared.Registry;

public class RemoteWebReaderService(WebReaderService.WebReaderServiceClient webReader)
{
    public async Task<(ModelsLinkEmbed LinkEmbed, string? Content)> ScrapeArticle(string url)
    {
        var request = new ScrapeArticleRequest { Url = url };
        var response = await webReader.ScrapeArticleAsync(request);
        return (
            LinkEmbed: response.Article?.LinkEmbed != null ? ModelsLinkEmbed.FromProtoValue(response.Article.LinkEmbed) : null!,
            Content: response.Article?.Content == "" ? null : response.Article?.Content
        );
    }

    public async Task<ModelsLinkEmbed> GetLinkPreview(string url, bool bypassCache = false)
    {
        var request = new GetLinkPreviewRequest { Url = url, BypassCache = bypassCache };
        var response = await webReader.GetLinkPreviewAsync(request);
        return response.Preview != null ? ModelsLinkEmbed.FromProtoValue(response.Preview) : null!;
    }

    public async Task<bool> InvalidateLinkPreviewCache(string url)
    {
        var request = new InvalidateLinkPreviewCacheRequest { Url = url };
        var response = await webReader.InvalidateLinkPreviewCacheAsync(request);
        return response.Success;
    }
}
