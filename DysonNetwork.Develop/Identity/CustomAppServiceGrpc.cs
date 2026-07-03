using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class CustomAppServiceGrpc(
    AppDatabase db,
    DyPublisherService.DyPublisherServiceClient publisherService
) : DyCustomAppService.DyCustomAppServiceBase
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
                var appBySlug = await q.FirstOrDefaultAsync(a => a.Slug.ToLower() == request.Slug.ToLowerInvariant());
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
        {
            var requestedType = request.IsOidc
                ? CustomAppSecretType.Oidc
                : CustomAppSecretType.ApiKey;
            var isOidc = requestedType == CustomAppSecretType.Oidc;
            q = q.Where(s => s.IsOidc == isOidc);
        }

        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
        var exists = await q.AnyAsync(s => s.Secret == request.Secret && (s.ExpiredAt == null || s.ExpiredAt > now));
        return new DyCheckCustomAppSecretResponse { Valid = exists };
    }

    public override async Task<DyGetAppProductResponse> GetAppProduct(DyGetAppProductRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Identifier))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "identifier required"));

        // Qualified identifier format: <publisherName>.<appSlug>.<sku>
        // SKU may contain dots, so split into exactly 3 parts: take first two, rest is SKU.
        var identifierParts = request.Identifier.Split('.');
        if (identifierParts.Length < 3)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid product identifier format"));

        var publisherName = identifierParts[0];
        var appSlug = identifierParts[1];
        var sku = string.Join(".", identifierParts[2..]);

        // Look up publisher by name
        var pubResponse = await publisherService.GetPublisherAsync(new DyGetPublisherRequest { Name = publisherName });
        var publisherId = Guid.Parse(pubResponse.Publisher.Id);

        // Look up the app: join CustomApps -> DevProjects -> Developers by slug
        var app = await db.CustomApps
            .Include(a => a.Project)
            .ThenInclude(p => p.Developer)
            .Where(a => a.Slug.ToLower() == appSlug.ToLowerInvariant())
            .Where(a => a.Project.Developer.PublisherId == publisherId)
            .FirstOrDefaultAsync();

        if (app is null)
            throw new RpcException(new Status(StatusCode.NotFound, "app not found for this publisher"));

        var product = await db.AppProducts
            .FirstOrDefaultAsync(p => p.AppId == app.Id && p.Identifier == sku);
        if (product is null)
            throw new RpcException(new Status(StatusCode.NotFound, "product not found"));

        return new DyGetAppProductResponse { Product = product.ToProto() };
    }
}
