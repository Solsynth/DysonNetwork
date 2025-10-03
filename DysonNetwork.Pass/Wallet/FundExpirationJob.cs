using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pass.Wallet;

public class FundExpirationJob(
    AppDatabase db,
    PaymentService paymentService,
    ILogger<FundExpirationJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting fund expiration job...");

        try
        {
            await paymentService.ProcessExpiredFundsAsync();
            logger.LogInformation("Successfully processed expired funds");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing expired funds");
        }
    }
}
