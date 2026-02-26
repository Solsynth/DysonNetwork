using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class CustomAppServiceGrpc(AppDatabase db) : DyCustomAppService.DyCustomAppServiceBase
{
    public override async Task<DyGetCustomAppResponse> GetCustomApp(DyGetCustomAppRequest request, ServerCallContext context)
    {
        var q = db.CustomApps.AsQueryable();
        switch (request.QueryCase)
        {
            case DyGetCustomAppRequest.QueryOneofCase.Id when !string.IsNullOrWhiteSpace(request.Id):
            {
                if (!Guid.TryParse(request.Id, out var id))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid id"));
                var appById = await q.FirstOrDefaultAsync(a => a.Id == id);
                if (appById is null)
                    throw new RpcException(new Status(StatusCode.NotFound, "app not found"));
                return new DyGetCustomAppResponse { App = appById.ToProto() };
            }
            case DyGetCustomAppRequest.QueryOneofCase.Slug when !string.IsNullOrWhiteSpace(request.Slug):
            {
                var appBySlug = await q.FirstOrDefaultAsync(a => a.Slug == request.Slug);
                if (appBySlug is null)
                    throw new RpcException(new Status(StatusCode.NotFound, "app not found"));
                return new DyGetCustomAppResponse { App = appBySlug.ToProto() };
            }
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, "id or slug required"));
        }
    }

    public override async Task<DyCheckCustomAppSecretResponse> CheckCustomAppSecret(DyCheckCustomAppSecretRequest request, ServerCallContext context)
    {
        if (string.IsNullOrEmpty(request.Secret))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "secret required"));

        IQueryable<SnCustomAppSecret> q = db.CustomAppSecrets;
        switch (request.SecretIdentifierCase)
        {
            case DyCheckCustomAppSecretRequest.SecretIdentifierOneofCase.SecretId:
            {
                if (!Guid.TryParse(request.SecretId, out var sid))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid secret_id"));
                q = q.Where(s => s.Id == sid);
                break;
            }
            case DyCheckCustomAppSecretRequest.SecretIdentifierOneofCase.AppId:
            {
                if (!Guid.TryParse(request.AppId, out var aid))
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid app_id"));
                q = q.Where(s => s.AppId == aid);
                break;
            }
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument, "secret_id or app_id required"));
        }

        if (request.HasIsOidc)
            q = q.Where(s => s.IsOidc == request.IsOidc);

        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
        var exists = await q.AnyAsync(s => s.Secret == request.Secret && (s.ExpiredAt == null || s.ExpiredAt > now));
        return new DyCheckCustomAppSecretResponse { Valid = exists };
    }
}
