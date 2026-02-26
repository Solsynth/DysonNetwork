using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Pass.Leveling;

public class ExperienceServiceGrpc(ExperienceService experienceService) : DyExperienceService.DyExperienceServiceBase
{
    public override async Task<DyExperienceRecord> AddRecord(DyAddExperienceRecordRequest request, ServerCallContext context)
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