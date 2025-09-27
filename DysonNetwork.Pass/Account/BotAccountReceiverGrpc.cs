using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;
using ApiKey = DysonNetwork.Shared.Proto.ApiKey;
using AuthService = DysonNetwork.Pass.Auth.AuthService;

namespace DysonNetwork.Pass.Account;

public class BotAccountReceiverGrpc(
    AppDatabase db,
    AccountService accounts,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    AuthService authService
)
    : BotAccountReceiverService.BotAccountReceiverServiceBase
{
    public override async Task<CreateBotAccountResponse> CreateBotAccount(
        CreateBotAccountRequest request,
        ServerCallContext context
    )
    {
        var account = SnAccount.FromProtoValue(request.Account);
        account = await accounts.CreateBotAccount(
            account,
            Guid.Parse(request.AutomatedId),
            request.PictureId,
            request.BackgroundId
        );

        return new CreateBotAccountResponse
        {
            Bot = new BotAccount
            {
                Account = account.ToProtoValue(),
                AutomatedId = account.Id.ToString(),
                CreatedAt = account.CreatedAt.ToTimestamp(),
                UpdatedAt = account.UpdatedAt.ToTimestamp(),
                IsActive = true
            }
        };
    }

    public override async Task<UpdateBotAccountResponse> UpdateBotAccount(
        UpdateBotAccountRequest request,
        ServerCallContext context
    )
    {
        var account = SnAccount.FromProtoValue(request.Account);

        if (request.PictureId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.PictureId });
            if (account.Profile.Picture is not null)
                await fileRefs.DeleteResourceReferencesAsync(
                    new DeleteResourceReferencesRequest { ResourceId = account.Profile.ResourceIdentifier }
                );
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    ResourceId = account.Profile.ResourceIdentifier,
                    FileId = request.PictureId,
                    Usage = "profile.picture"
                }
            );
            account.Profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.BackgroundId });
            if (account.Profile.Background is not null)
                await fileRefs.DeleteResourceReferencesAsync(
                    new DeleteResourceReferencesRequest { ResourceId = account.Profile.ResourceIdentifier }
                );
            await fileRefs.CreateReferenceAsync(
                new CreateReferenceRequest
                {
                    ResourceId = account.Profile.ResourceIdentifier,
                    FileId = request.BackgroundId,
                    Usage = "profile.background"
                }
            );
            account.Profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        db.Accounts.Update(account);
        await db.SaveChangesAsync();

        return new UpdateBotAccountResponse
        {
            Bot = new BotAccount
            {
                Account = account.ToProtoValue(),
                AutomatedId = account.Id.ToString(),
                CreatedAt = account.CreatedAt.ToTimestamp(),
                UpdatedAt = account.UpdatedAt.ToTimestamp(),
                IsActive = true
            }
        };
    }

    public override async Task<DeleteBotAccountResponse> DeleteBotAccount(
        DeleteBotAccountRequest request,
        ServerCallContext context
    )
    {
        var automatedId = Guid.Parse(request.AutomatedId);
        var account = await accounts.GetBotAccount(automatedId);
        if (account is null)
            throw new RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.NotFound, "Account not found"));

        await accounts.DeleteAccount(account);

        return new DeleteBotAccountResponse();
    }

    public override async Task<ApiKey> GetApiKey(GetApiKeyRequest request, ServerCallContext context)
    {
        var keyId = Guid.Parse(request.Id);
        var key = await db.ApiKeys
            .Include(k => k.Account)
            .FirstOrDefaultAsync(k => k.Id == keyId);

        if (key == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "API key not found"));

        return key.ToProtoValue();
    }

    public override async Task<GetApiKeyBatchResponse> ListApiKey(ListApiKeyRequest request, ServerCallContext context)
    {
        var automatedId = Guid.Parse(request.AutomatedId);
        var account = await accounts.GetBotAccount(automatedId);
        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Account not found"));

        var keys = await db.ApiKeys
            .Where(k => k.AccountId == account.Id)
            .Select(k => k.ToProtoValue())
            .ToListAsync();

        var response = new GetApiKeyBatchResponse();
        response.Data.AddRange(keys);
        return response;
    }

    public override async Task<ApiKey> CreateApiKey(ApiKey request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var account = await accounts.GetBotAccount(accountId);
        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Account not found"));

        if (string.IsNullOrWhiteSpace(request.Label))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Label is required"));

        var key = await authService.CreateApiKey(account.Id, request.Label, null);
        key.Key = await authService.IssueApiKeyToken(key);
        
        return key.ToProtoValue();
    }

    public override async Task<ApiKey> UpdateApiKey(ApiKey request, ServerCallContext context)
    {
        var keyId = Guid.Parse(request.Id);
        var accountId = Guid.Parse(request.AccountId);
        
        var key = await db.ApiKeys
            .Include(k => k.Session)
            .Where(k => k.Id == keyId && k.AccountId == accountId)
            .FirstOrDefaultAsync();
            
        if (key == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "API key not found"));

        // Only update the label if provided
        if (string.IsNullOrWhiteSpace(request.Label)) return key.ToProtoValue();
        key.Label = request.Label;
        db.ApiKeys.Update(key);
        await db.SaveChangesAsync();

        return key.ToProtoValue();
    }

    public override async Task<ApiKey> RotateApiKey(GetApiKeyRequest request, ServerCallContext context)
    {
        var keyId = Guid.Parse(request.Id);
        var key = await db.ApiKeys
            .Include(k => k.Session)
            .FirstOrDefaultAsync(k => k.Id == keyId);
            
        if (key == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "API key not found"));

        key = await authService.RotateApiKeyToken(key);
        key.Key = await authService.IssueApiKeyToken(key);
        
        return key.ToProtoValue();
    }

    public override async Task<DeleteApiKeyResponse> DeleteApiKey(GetApiKeyRequest request, ServerCallContext context)
    {
        var keyId = Guid.Parse(request.Id);
        var key = await db.ApiKeys
            .Include(k => k.Session)
            .FirstOrDefaultAsync(k => k.Id == keyId);
            
        if (key == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "API key not found"));

        await authService.RevokeApiKeyToken(key);
        
        return new DeleteApiKeyResponse { Success = true };
    }
}