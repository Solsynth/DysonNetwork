using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Insight.Reader;

public class WebReaderGrpcService(WebReaderService service) : Shared.Proto.WebReaderService.WebReaderServiceBase
{
    public override async Task<ScrapeArticleResponse> ScrapeArticle(
        ScrapeArticleRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "url is required"));

        var scrapedArticle = await service.ScrapeArticleAsync(request.Url, context.CancellationToken);
        return new ScrapeArticleResponse { Article = scrapedArticle.ToProtoValue() };
    }

    public override async Task<GetLinkPreviewResponse> GetLinkPreview(
        GetLinkPreviewRequest request,
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

        return new GetLinkPreviewResponse { Preview = linkEmbed.ToProtoValue() };
    }

    public override async Task<InvalidateLinkPreviewCacheResponse> InvalidateLinkPreviewCache(
        InvalidateLinkPreviewCacheRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "url is required"));

        await service.InvalidateCacheForUrlAsync(request.Url);

        return new InvalidateLinkPreviewCacheResponse { Success = true };
    }
}
