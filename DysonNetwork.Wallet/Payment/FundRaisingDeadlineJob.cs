using Quartz;

namespace DysonNetwork.Wallet.Payment;

public class FundRaisingDeadlineJob(
    PaymentService paymentService,
    ILogger<FundRaisingDeadlineJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting fund raising deadline job...");

        try
        {
            await paymentService.ProcessExpiredRaisingFundsAsync();
            logger.LogInformation("Successfully processed expired raising funds");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing expired raising funds");
        }
    }
}
