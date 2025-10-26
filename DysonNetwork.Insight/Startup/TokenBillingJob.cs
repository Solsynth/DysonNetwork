using DysonNetwork.Insight.Thought;
using Quartz;

namespace DysonNetwork.Insight.Startup;

public class TokenBillingJob(ThoughtService thoughtService, ILogger<TokenBillingJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await thoughtService.SettleThoughtBills(logger);
    }
}
