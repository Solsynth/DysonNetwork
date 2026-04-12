using DysonNetwork.Insight.MiChan;
using Quartz;

namespace DysonNetwork.Insight.Thought;

public class FreeQuotaResetJob(FreeQuotaService freeQuotaService, ILogger<FreeQuotaResetJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting daily free quota reset job");
        await freeQuotaService.ResetAllQuotasAsync();
        logger.LogInformation("Daily free quota reset completed");
    }
}