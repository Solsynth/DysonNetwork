using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace DysonNetwork.Drive.Storage;

public class FileServiceGrpc(FileService fileService) : DyFileService.DyFileServiceBase
{
    public override async Task<DyCloudFile> GetFile(DyGetFileRequest request, ServerCallContext context)
    {
        var file = await fileService.GetFileAsync(request.Id);
        return file?.ToProtoValue() ?? throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
    }

    public override async Task<DyGetFileBatchResponse> GetFileBatch(DyGetFileBatchRequest request,
        ServerCallContext context)
    {
        var files = await fileService.GetFilesAsync(request.Ids.ToList());
        return new DyGetFileBatchResponse { Files = { files.Select(f => f.ToProtoValue()) } };
    }

    public override async Task<DyCloudFile> UpdateFile(
        DyUpdateFileRequest request,
        ServerCallContext context
    )
    {
        var file = await fileService.GetFileAsync(request.File.Id);
        if (file == null)
            throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
        var updatedFile = await fileService.UpdateFileAsync(file, request.UpdateMask);
        return updatedFile.ToProtoValue();
    }

    public override async Task<Empty> DeleteFile(DyDeleteFileRequest request, ServerCallContext context)
    {
        var file = await fileService.GetFileAsync(request.Id);
        if (file == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "File not found"));
        }

        await fileService.DeleteFileAsync(file);
        return new Empty();
    }

    public override async Task<Empty> PurgeCache(DyPurgeCacheRequest request, ServerCallContext context)
    {
        await fileService._PurgeCacheAsync(request.FileId);
        return new Empty();
    }

    public override async Task<Empty> SetFilePublic(DySetFilePublicRequest request, ServerCallContext context)
    {
        await fileService.SetPublicAsync(request.FileId);
        return new Empty();
    }

    public override async Task<Empty> UnsetFilePublic(DyUnsetFilePublicRequest request, ServerCallContext context)
    {
        await fileService.UnsetPublicAsync(request.FileId);
        return new Empty();
    }
}