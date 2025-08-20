using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Pass.Leveling;

public class ExperienceServiceGrpc(ExperienceService experienceService) : Shared.Proto.ExperienceService.ExperienceServiceBase
{
    public override async Task<Shared.Proto.ExperienceRecord> AddRecord(AddExperienceRecordRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var record = await experienceService.AddRecord(
            request.ReasonType,
            request.Reason,
            request.Delta,
            accountId);

        return record.ToProto();
    }
}