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

        public override async Task<LoadFromReferenceResponse> LoadFromReference(
            LoadFromReferenceRequest request,
            ServerCallContext context
        )
        {
            // Assuming CloudFileReferenceObject is a simple class/struct that holds an ID
            // You might need to define this or adjust the LoadFromReference method in FileService
            var references = request.ReferenceIds.Select(id => new CloudFileReferenceObject { Id = id }).ToList();
            var files = await fileService.LoadFromReference(references);
            var response = new LoadFromReferenceResponse();
            response.Files.AddRange(files.Where(f => f != null).Select(f => f!.ToProtoValue()));
            return response;
        }

        public override async Task<IsReferencedResponse> IsReferenced(IsReferencedRequest request,
            ServerCallContext context)
        {
            var isReferenced = await fileService.IsReferencedAsync(request.FileId);
            return new IsReferencedResponse { IsReferenced = isReferenced };
        }

        public override async Task<Empty> PurgeCache(PurgeCacheRequest request, ServerCallContext context)
        {
            await fileService._PurgeCacheAsync(request.FileId);
            return new Empty();
        }
    }
}