using DysonNetwork.Shared.Proto;
using ModelsLinkEmbed = DysonNetwork.Shared.Models.Embed.LinkEmbed;

namespace DysonNetwork.Shared.Registry;

public class RemoteWebReaderService(DyWebReaderService.DyWebReaderServiceClient webReader)
{
    public async Task<(ModelsLinkEmbed LinkEmbed, string? Content)> ScrapeArticle(string url)
    {
        var request = new DyScrapeArticleRequest { Url = url };
        var response = await webReader.ScrapeArticleAsync(request);
        return (
            LinkEmbed: response.Article?.LinkEmbed != null ? ModelsLinkEmbed.FromProtoValue(response.Article.LinkEmbed) : null!,
            Content: response.Article?.Content == "" ? null : response.Article?.Content
        );
    }

    public async Task<ModelsLinkEmbed> GetLinkPreview(string url, bool bypassCache = false)
    {
        var request = new DyGetLinkPreviewRequest { Url = url, BypassCache = bypassCache };
        var response = await webReader.GetLinkPreviewAsync(request);
        return response.Preview != null ? ModelsLinkEmbed.FromProtoValue(response.Preview) : null!;
    }

    public async Task<bool> InvalidateLinkPreviewCache(string url)
    {
        var request = new DyInvalidateLinkPreviewCacheRequest { Url = url };
        var response = await webReader.InvalidateLinkPreviewCacheAsync(request);
        return response.Success;
    }
}
