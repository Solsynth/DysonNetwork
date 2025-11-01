using Quartz;

namespace DysonNetwork.Pass.Credit;

public class SocialCreditValidationJob(SocialCreditService socialCreditService, ILogger<SocialCreditValidationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting social credit validation...");

        try
        {
            await socialCreditService.ValidateSocialCredits();
            logger.LogInformation("Social credit validation completed successfully.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during social credit validation.");
        }
    }
}
