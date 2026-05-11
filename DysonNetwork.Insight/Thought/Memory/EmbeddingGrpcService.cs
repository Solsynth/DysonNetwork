using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Insight.Thought.Memory;

public class EmbeddingGrpcService(EmbeddingService embeddingService)
    : DyEmbeddingService.DyEmbeddingServiceBase
{
    public override async Task<DyGenerateEmbeddingResponse> GenerateEmbedding(
        DyGenerateEmbeddingRequest request,
        ServerCallContext context
    )
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "text is required"));

        var embedding = await embeddingService.GenerateEmbeddingAsync(
            request.Text,
            context.CancellationToken
        );
        if (embedding is null)
            throw new RpcException(
                new Status(StatusCode.FailedPrecondition, "embedding generation unavailable")
            );

        var response = new DyGenerateEmbeddingResponse();
        response.Embedding.AddRange(embedding.ToArray());
        response.Dimensions = embedding.ToArray().Length;
        return response;
    }

    public override async Task<DyGenerateEmbeddingsResponse> GenerateEmbeddings(
        DyGenerateEmbeddingsRequest request,
        ServerCallContext context
    )
    {
        if (request.Texts.Count == 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "texts is required"));

        var embeddings = await embeddingService.GenerateEmbeddingsAsync(
            request.Texts,
            context.CancellationToken
        );

        var response = new DyGenerateEmbeddingsResponse();
        foreach (var embedding in embeddings)
        {
            if (embedding is null)
                throw new RpcException(
                    new Status(StatusCode.FailedPrecondition, "embedding generation unavailable")
                );

            var values = embedding.ToArray();
            response.Embeddings.Add(
                new DyEmbeddingItem
                {
                    Dimensions = values.Length,
                    Embedding = { values },
                }
            );
        }

        return response;
    }
}
