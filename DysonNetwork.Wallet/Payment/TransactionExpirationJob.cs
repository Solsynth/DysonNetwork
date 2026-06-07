using Quartz;

namespace DysonNetwork.Wallet.Payment;

public class TransactionExpirationJob(
    PaymentService paymentService,
    ILogger<TransactionExpirationJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting transaction expiration job...");

        try
        {
            await paymentService.ProcessExpiredTransactionsAsync();
            logger.LogInformation("Successfully processed expired transactions");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing expired transactions");
        }
    }
}
