using DysonNetwork.Shared.Proto;
using Grpc.Core;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Account;

public class BotAccountReceiverGrpc(AppDatabase db, AccountService accounts)
    : BotAccountReceiverService.BotAccountReceiverServiceBase
{
    public override async Task<CreateBotAccountResponse> CreateBotAccount(
        CreateBotAccountRequest request,
        ServerCallContext context
    )
    {
        var account = Account.FromProtoValue(request.Account);
        account = await accounts.CreateBotAccount(account, Guid.Parse(request.AutomatedId));

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
        var automatedId = Guid.Parse(request.AutomatedId);
        var account = await accounts.GetBotAccount(automatedId);
        if (account is null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Account not found"));

        account.Name = request.Account.Name;
        account.Nick = request.Account.Nick;
        account.Profile = AccountProfile.FromProtoValue(request.Account.Profile);
        account.Language = request.Account.Language;
        
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