using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Drive.Storage
{
    public class FileServiceGrpc(FileService fileService) : Shared.Proto.FileService.FileServiceBase
    {
        public override async Task<Shared.Proto.CloudFile> GetFile(GetFileRequest request, ServerCallContext context)
        {
            var file = await fileService.GetFileAsync(request.Id);
            return file?.ToProtoValue() ?? throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
        }

        public override async Task<GetFileBatchResponse> GetFileBatch(GetFileBatchRequest request, ServerCallContext context)
        {
            var files = await fileService.GetFilesAsync(request.Ids.ToList());
            return new GetFileBatchResponse { Files = { files.Select(f => f.ToProtoValue()) } };
        }

        public override async Task<Shared.Proto.CloudFile> UpdateFile(UpdateFileRequest request,
            ServerCallContext context)
        {
            var file = await fileService.GetFileAsync(request.File.Id);
            if (file == null)
                throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
            var updatedFile = await fileService.UpdateFileAsync(file, request.UpdateMask);
            return updatedFile.ToProtoValue();
        }

        public override async Task<Empty> DeleteFile(DeleteFileRequest request, ServerCallContext context)
        {
            var file = await fileService.GetFileAsync(request.Id);
            if (file == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
            }

            await fileService.DeleteFileAsync(file);
            return new Empty();
        }

        public override async Task<Empty> PurgeCache(PurgeCacheRequest request, ServerCallContext context)
        {
            await fileService._PurgeCacheAsync(request.FileId);
            return new Empty();
        }
    }
}