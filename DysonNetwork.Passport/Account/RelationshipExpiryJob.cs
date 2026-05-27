using Quartz;

namespace DysonNetwork.Passport.Account;

public class RelationshipExpiryJob(
    RelationshipService relationshipService,
    ILogger<RelationshipExpiryJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var processed = await relationshipService.ProcessExpiredRelationshipsAsync(context.CancellationToken);
        if (processed > 0)
            logger.LogInformation("Relationship expiry processed {Count} expired relationships.", processed);
    }
}
