using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Sphere.Sticker;

public class StickerServiceGrpc(StickerService stickerService) : DyStickerService.DyStickerServiceBase
{
    public override async Task<DySticker> GetStickerByIdentifier(DyGetStickerRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "identifier is required"));

        var sticker = await stickerService.LookupStickerByIdentifierAsync(request.Identifier);
        if (sticker is null)
            throw new RpcException(new Status(StatusCode.NotFound, "sticker not found"));

        return sticker.ToProtoValue();
    }
}
