using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;
using AuthService = DysonNetwork.Pass.Auth.AuthService;

namespace DysonNetwork.Pass.Account;

public class BotAccountReceiverGrpc(
    AppDatabase db,
    AccountService accounts,
    DyFileService.DyFileServiceClient files,
    AuthService authService
)
    : DyBotAccountReceiverService.DyBotAccountReceiverServiceBase
{
    public override async Task<DyCreateBotAccountResponse> CreateBotAccount(
        DyCreateBotAccountRequest request,
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

        return new DyCreateBotAccountResponse
        {
            Bot = new DyBotAccount
            {
                Account = account.ToProtoValue(),
                AutomatedId = account.Id.ToString(),
                CreatedAt = account.CreatedAt.ToTimestamp(),
                UpdatedAt = account.UpdatedAt.ToTimestamp(),
                IsActive = true
            }
        };
    }

    public override async Task<DyUpdateBotAccountResponse> UpdateBotAccount(
        DyUpdateBotAccountRequest request,
        ServerCallContext context
    )
    {
        var account = SnAccount.FromProtoValue(request.Account);

        if (request.PictureId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            account.Profile.Picture = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        if (request.BackgroundId is not null)
        {
            var file = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            account.Profile.Background = SnCloudFileReferenceObject.FromProtoValue(file);
        }

        db.Accounts.Update(account);
        await db.SaveChangesAsync();

        return new DyUpdateBotAccountResponse
        {
            Bot = new DyBotAccount
            {
                Account = account.ToProtoValue(),
                AutomatedId = account.Id.ToString(),
                CreatedAt = account.CreatedAt.ToTimestamp(),
                UpdatedAt = account.UpdatedAt.ToTimestamp(),
                IsActive = true
            }
        };
    }

    public override async Task<DyDeleteBotAccountResponse> DeleteBotAccount(
        DyDeleteBotAccountRequest request,
        ServerCallContext context
    )
    {
        var automatedId = Guid.Parse(request.AutomatedId);
        var account = await accounts.GetBotAccount(automatedId);
        if (account is null)
            throw new RpcException(new Status(StatusCode.NotFound, "Account not found"));

        await accounts.DeleteAccount(account);

        return new DyDeleteBotAccountResponse();
    }

    public override async Task<DyApiKey> GetApiKey(DyGetApiKeyRequest request, ServerCallContext context)
    {
        var keyId = Guid.Parse(request.Id);
        var key = await db.ApiKeys
            .Include(k => k.Account)
            .FirstOrDefaultAsync(k => k.Id == keyId);

        if (key == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "API key not found"));

        return key.ToProtoValue();
    }

    public override async Task<DyGetApiKeyBatchResponse> ListApiKey(DyListApiKeyRequest request, ServerCallContext context)
    {
        var automatedId = Guid.Parse(request.AutomatedId);
        var account = await accounts.GetBotAccount(automatedId);
        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Account not found"));

        var keys = await db.ApiKeys
            .Where(k => k.AccountId == account.Id)
            .Select(k => k.ToProtoValue())
            .ToListAsync();

        var response = new DyGetApiKeyBatchResponse();
        response.Data.AddRange(keys);
        return response;
    }

    public override async Task<DyApiKey> CreateApiKey(DyApiKey request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var account = await accounts.GetBotAccount(accountId);
        if (account == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Account not found"));

        if (string.IsNullOrWhiteSpace(request.Label))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Label is required"));

        var key = await authService.CreateApiKey(account.Id, request.Label, null);
        key.Key = await authService.IssueApiKeyToken(key);
        
        return key.ToProtoValue();
    }

    public override async Task<DyApiKey> UpdateApiKey(DyApiKey request, ServerCallContext context)
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

    public override async Task<DyApiKey> RotateApiKey(DyGetApiKeyRequest request, ServerCallContext context)
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

    public override async Task<DyDeleteApiKeyResponse> DeleteApiKey(DyGetApiKeyRequest request, ServerCallContext context)
    {
        var keyId = Guid.Parse(request.Id);
        var key = await db.ApiKeys
            .Include(k => k.Session)
            .FirstOrDefaultAsync(k => k.Id == keyId);
            
        if (key == null)
            throw new RpcException(new Status(StatusCode.NotFound, "API key not found"));

        await authService.RevokeApiKeyToken(key);
        
        return new DyDeleteApiKeyResponse { Success = true };
    }
}