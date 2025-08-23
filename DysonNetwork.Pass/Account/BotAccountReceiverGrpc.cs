using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Account;

public class BotAccountReceiverGrpc(
    AppDatabase db,
    AccountService accounts,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs
)
    : BotAccountReceiverService.BotAccountReceiverServiceBase
{
    public override async Task<CreateBotAccountResponse> CreateBotAccount(
        CreateBotAccountRequest request,
        ServerCallContext context
    )
    {
        var account = Account.FromProtoValue(request.Account);
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
        var account = Account.FromProtoValue(request.Account);

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
            account.Profile.Picture = CloudFileReferenceObject.FromProtoValue(file);
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
            account.Profile.Background = CloudFileReferenceObject.FromProtoValue(file);
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
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Account not found"));

        await accounts.DeleteAccount(account);

        return new DeleteBotAccountResponse();
    }
}