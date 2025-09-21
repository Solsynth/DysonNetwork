using DysonNetwork.Shared.Proto;
using Grpc.Core;
using NodaTime;
using Duration = NodaTime.Duration;

namespace DysonNetwork.Drive.Storage;

public class FileReferenceServiceGrpc(FileReferenceService fileReferenceService)
    : Shared.Proto.FileReferenceService.FileReferenceServiceBase
{
    public override async Task<Shared.Proto.CloudFileReference> CreateReference(CreateReferenceRequest request,
        ServerCallContext context)
    {
        Instant? expiredAt = null;
        if (request.ExpiredAt != null)
            expiredAt = Instant.FromUnixTimeSeconds(request.ExpiredAt.Seconds);
        else if (request.Duration != null)
            expiredAt = SystemClock.Instance.GetCurrentInstant() +
                        Duration.FromTimeSpan(request.Duration.ToTimeSpan());

        var reference = await fileReferenceService.CreateReferenceAsync(
            request.FileId,
            request.Usage,
            request.ResourceId,
            expiredAt
        );
        return reference.ToProtoValue();
    }

    public override async Task<CreateReferenceBatchResponse> CreateReferenceBatch(CreateReferenceBatchRequest request,
        ServerCallContext context)
    {
        Instant? expiredAt = null;
        if (request.ExpiredAt != null)
            expiredAt = Instant.FromUnixTimeSeconds(request.ExpiredAt.Seconds);
        else if (request.Duration != null)
            expiredAt = SystemClock.Instance.GetCurrentInstant() +
                        Duration.FromTimeSpan(request.Duration.ToTimeSpan());

        var references = await fileReferenceService.CreateReferencesAsync(
            request.FilesId.ToList(),
            request.Usage,
            request.ResourceId,
            expiredAt
        );
        var response = new CreateReferenceBatchResponse();
        response.References.AddRange(references.Select(r => r.ToProtoValue()));
        return response;
    }

    public override async Task<GetReferencesResponse> GetReferences(GetReferencesRequest request,
        ServerCallContext context)
    {
        var references = await fileReferenceService.GetReferencesAsync(request.FileId);
        var response = new GetReferencesResponse();
        response.References.AddRange(references.Select(r => r.ToProtoValue()));
        return response;
    }

    public override async Task<GetReferenceCountResponse> GetReferenceCount(GetReferenceCountRequest request,
        ServerCallContext context)
    {
        var count = await fileReferenceService.GetReferenceCountAsync(request.FileId);
        return new GetReferenceCountResponse { Count = count };
    }

    public override async Task<GetReferencesResponse> GetResourceReferences(GetResourceReferencesRequest request,
        ServerCallContext context)
    {
        var references = await fileReferenceService.GetResourceReferencesAsync(request.ResourceId, request.Usage);
        var response = new GetReferencesResponse();
        response.References.AddRange(references.Select(r => r.ToProtoValue()));
        return response;
    }

    public override async Task<GetResourceFilesResponse> GetResourceFiles(GetResourceFilesRequest request,
        ServerCallContext context)
    {
        var files = await fileReferenceService.GetResourceFilesAsync(request.ResourceId, request.Usage);
        var response = new GetResourceFilesResponse();
        response.Files.AddRange(files.Select(f => f.ToProtoValue()));
        return response;
    }

    public override async Task<DeleteResourceReferencesResponse> DeleteResourceReferences(
        DeleteResourceReferencesRequest request, ServerCallContext context)
    {
        int deletedCount;
        if (request.Usage is null)
            deletedCount = await fileReferenceService.DeleteResourceReferencesAsync(request.ResourceId);
        else
            deletedCount =
                await fileReferenceService.DeleteResourceReferencesAsync(request.ResourceId, request.Usage!);
        return new DeleteResourceReferencesResponse { DeletedCount = deletedCount };
    }
        
    public override async Task<DeleteResourceReferencesResponse> DeleteResourceReferencesBatch(DeleteResourceReferencesBatchRequest request, ServerCallContext context)
    {
        var resourceIds = request.ResourceIds.ToList();
        int deletedCount;
        if (request.Usage is null)
            deletedCount = await fileReferenceService.DeleteResourceReferencesBatchAsync(resourceIds);
        else
            deletedCount =
                await fileReferenceService.DeleteResourceReferencesBatchAsync(resourceIds, request.Usage!);
        return new DeleteResourceReferencesResponse { DeletedCount = deletedCount };
    }

    public override async Task<DeleteReferenceResponse> DeleteReference(DeleteReferenceRequest request,
        ServerCallContext context)
    {
        var success = await fileReferenceService.DeleteReferenceAsync(Guid.Parse(request.ReferenceId));
        return new DeleteReferenceResponse { Success = success };
    }

    public override async Task<UpdateResourceFilesResponse> UpdateResourceFiles(UpdateResourceFilesRequest request,
        ServerCallContext context)
    {
        Instant? expiredAt = null;
        if (request.ExpiredAt != null)
        {
            expiredAt = Instant.FromUnixTimeSeconds(request.ExpiredAt.Seconds);
        }
        else if (request.Duration != null)
        {
            expiredAt = SystemClock.Instance.GetCurrentInstant() +
                        Duration.FromTimeSpan(request.Duration.ToTimeSpan());
        }

        var references = await fileReferenceService.UpdateResourceFilesAsync(
            request.ResourceId,
            request.FileIds,
            request.Usage,
            expiredAt
        );
        var response = new UpdateResourceFilesResponse();
        response.References.AddRange(references.Select(r => r.ToProtoValue()));
        return response;
    }

    public override async Task<SetReferenceExpirationResponse> SetReferenceExpiration(
        SetReferenceExpirationRequest request, ServerCallContext context)
    {
        Instant? expiredAt = null;
        if (request.ExpiredAt != null)
        {
            expiredAt = Instant.FromUnixTimeSeconds(request.ExpiredAt.Seconds);
        }
        else if (request.Duration != null)
        {
            expiredAt = SystemClock.Instance.GetCurrentInstant() +
                        Duration.FromTimeSpan(request.Duration.ToTimeSpan());
        }

        var success =
            await fileReferenceService.SetReferenceExpirationAsync(Guid.Parse(request.ReferenceId), expiredAt);
        return new SetReferenceExpirationResponse { Success = success };
    }

    public override async Task<SetFileReferencesExpirationResponse> SetFileReferencesExpiration(
        SetFileReferencesExpirationRequest request, ServerCallContext context)
    {
        var expiredAt = Instant.FromUnixTimeSeconds(request.ExpiredAt.Seconds);
        var updatedCount = await fileReferenceService.SetFileReferencesExpirationAsync(request.FileId, expiredAt);
        return new SetFileReferencesExpirationResponse { UpdatedCount = updatedCount };
    }

    public override async Task<HasFileReferencesResponse> HasFileReferences(HasFileReferencesRequest request,
        ServerCallContext context)
    {
        var hasReferences = await fileReferenceService.HasFileReferencesAsync(request.FileId);
        return new HasFileReferencesResponse { HasReferences = hasReferences };
    }
}