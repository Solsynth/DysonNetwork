using Quartz;

namespace DysonNetwork.Pass.Lotteries;

public class LotteryDrawJob(LotteryService lotteryService, ILogger<LotteryDrawJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting daily lottery draw...");

        try
        {
            await lotteryService.DrawLotteries();
            logger.LogInformation("Daily lottery draw completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during daily lottery draw.");
        }
    }
}
