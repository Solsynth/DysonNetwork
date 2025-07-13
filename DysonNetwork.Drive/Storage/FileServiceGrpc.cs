using System.Threading.Tasks;
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

        public override async Task<Shared.Proto.CloudFile> UpdateFile(UpdateFileRequest request, ServerCallContext context)
        {
            // Assuming UpdateFileAsync exists in FileService and handles the update_mask
            // This is a placeholder, as the current FileService.cs doesn't have a direct UpdateFile method
            // You might need to implement this logic in FileService based on your needs.
            // For now, we'll just return the requested file.
            var file = await fileService.GetFileAsync(request.File.Id);
            if (file == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
            }

            // Apply updates from request.File to 'file' based on request.UpdateMask
            // This part requires more detailed implementation based on how you want to handle partial updates.
            return file.ToProtoValue();
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

        public override async Task<Shared.Proto.CloudFile> ProcessNewFile(IAsyncStreamReader<ProcessNewFileRequest> requestStream,
            ServerCallContext context)
        {
            ProcessNewFileRequest? metadataRequest = null;
            var chunks = new List<byte[]>();

            await foreach (var message in requestStream.ReadAllAsync())
            {
                if (message.DataCase == ProcessNewFileRequest.DataOneofCase.Metadata)
                {
                    metadataRequest = message;
                }
                else if (message.DataCase == ProcessNewFileRequest.DataOneofCase.Chunk)
                {
                    chunks.Add(message.Chunk.ToByteArray());
                }
            }

            if (metadataRequest == null || metadataRequest.Metadata == null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing file metadata"));
            }

            var metadata = metadataRequest.Metadata;
            using var memoryStream = new MemoryStream();
            foreach (var chunk in chunks)
            {
                await memoryStream.WriteAsync(chunk);
            }

            memoryStream.Position = 0;

            // Assuming you have an Account object available or can create a dummy one for now
            // You might need to adjust this based on how accounts are handled in your system
            var dummyAccount = new Account { Id = metadata.AccountId };

            var cloudFile = await fileService.ProcessNewFileAsync(
                dummyAccount,
                metadata.FileId,
                memoryStream,
                metadata.FileName,
                metadata.ContentType
            );
            return cloudFile.ToProtoValue();
        }

        public override async Task<Shared.Proto.CloudFile> UploadFileToRemote(
            IAsyncStreamReader<UploadFileToRemoteRequest> requestStream, ServerCallContext context)
        {
            UploadFileToRemoteRequest? metadataRequest = null;
            var chunks = new List<byte[]>();

            await foreach (var message in requestStream.ReadAllAsync())
            {
                if (message.DataCase == UploadFileToRemoteRequest.DataOneofCase.Metadata)
                {
                    metadataRequest = message;
                }
                else if (message.DataCase == UploadFileToRemoteRequest.DataOneofCase.Chunk)
                {
                    chunks.Add(message.Chunk.ToByteArray());
                }
            }

            if (metadataRequest == null || metadataRequest.Metadata == null)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing upload metadata"));
            }

            var metadata = metadataRequest.Metadata;
            using var memoryStream = new MemoryStream();
            foreach (var chunk in chunks)
            {
                await memoryStream.WriteAsync(chunk);
            }

            memoryStream.Position = 0;

            var file = await fileService.GetFileAsync(metadata.FileId);
            if (file == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
            }

            var uploadedFile = await fileService.UploadFileToRemoteAsync(
                file,
                memoryStream,
                metadata.TargetRemote,
                metadata.Suffix
            );
            return uploadedFile.ToProtoValue();
        }

        public override async Task<Empty> DeleteFileData(DeleteFileDataRequest request, ServerCallContext context)
        {
            var file = await fileService.GetFileAsync(request.FileId);
            if (file == null)
            {
                throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
            }

            await fileService.DeleteFileDataAsync(file);
            return new Empty();
        }

        public override async Task<LoadFromReferenceResponse> LoadFromReference(LoadFromReferenceRequest request,
            ServerCallContext context)
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