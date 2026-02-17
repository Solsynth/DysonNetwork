using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Pass.Credit;

public class SocialCreditValidationJob(AppDatabase db, ICacheService cache, ILogger<SocialCreditValidationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting social credit cache invalidation...");

        try
        {
            await cache.RemoveGroupAsync("account:");
            logger.LogInformation("Social credit cache invalidation completed.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during social credit cache invalidation.");
        }
    }
}
