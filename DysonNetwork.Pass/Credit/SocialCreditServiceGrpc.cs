using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Pass.Credit;

public class SocialCreditServiceGrpc(SocialCreditService creditService) : Shared.Proto.SocialCreditService.SocialCreditServiceBase
{
    public override async Task<Shared.Proto.SocialCreditRecord> AddRecord(AddSocialCreditRecordRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var record = await creditService.AddRecord(
            request.ReasonType,
            request.Reason,
            request.Delta,
            accountId);

        return record.ToProto();
    }

    public override async Task<SocialCreditResponse> GetSocialCredit(GetSocialCreditRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var amount = await creditService.GetSocialCredit(accountId);
        
        return new SocialCreditResponse { Amount = amount };
    }
}