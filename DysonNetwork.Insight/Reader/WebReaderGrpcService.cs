using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Insight.Reader;

public class WebReaderGrpcService(WebReaderService service) : DyWebReaderService.DyWebReaderServiceBase
{
    public override async Task<DyScrapeArticleResponse> ScrapeArticle(
        DyScrapeArticleRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "url is required"));

        var scrapedArticle = await service.ScrapeArticleAsync(request.Url, context.CancellationToken);
        return new DyScrapeArticleResponse { Article = scrapedArticle.ToProtoValue() };
    }

    public override async Task<DyGetLinkPreviewResponse> GetLinkPreview(
        DyGetLinkPreviewRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "url is required"));

        var linkEmbed = await service.GetLinkPreviewAsync(
            request.Url,
            context.CancellationToken,
            bypassCache: request.BypassCache
        );

        return new DyGetLinkPreviewResponse { Preview = linkEmbed.ToProtoValue() };
    }

    public override async Task<DyInvalidateLinkPreviewCacheResponse> InvalidateLinkPreviewCache(
        DyInvalidateLinkPreviewCacheRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "url is required"));

        await service.InvalidateCacheForUrlAsync(request.Url);

        return new DyInvalidateLinkPreviewCacheResponse { Success = true };
    }
}
