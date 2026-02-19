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
        logger.LogInformation("Starting social credit profile update...");

        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            const double baseSocialCredit = 100;

            var accountIds = await db.SocialCreditRecords
                .Where(r => !r.DeletedAt.HasValue)
                .Select(r => r.AccountId)
                .Distinct()
                .ToListAsync();

            var processed = 0;
            const int batchSize = 100;

            for (var i = 0; i < accountIds.Count; i += batchSize)
            {
                var batchIds = accountIds.Skip(i).Take(batchSize).ToList();

                var records = await db.SocialCreditRecords
                    .Where(r => batchIds.Contains(r.AccountId) && !r.DeletedAt.HasValue)
                    .Select(r => new { r.AccountId, r.Delta, r.CreatedAt, r.ExpiredAt })
                    .ToListAsync();

                var effectiveCredits = records
                    .GroupBy(r => r.AccountId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(r =>
                        {
                            if (r.ExpiredAt.HasValue && r.ExpiredAt <= now)
                                return 0;
                            if (!r.ExpiredAt.HasValue)
                                return r.Delta;
                            var totalDuration = r.ExpiredAt.Value - r.CreatedAt;
                            if (totalDuration == Duration.Zero)
                                return r.Delta;
                            var elapsed = now - r.CreatedAt;
                            var remainingRatio = 1.0 - (elapsed.TotalSeconds / totalDuration.TotalSeconds);
                            return r.Delta * Math.Max(0, remainingRatio);
                        }) + baseSocialCredit
                    );

                foreach (var (accountId, credit) in effectiveCredits)
                {
                    await db.AccountProfiles
                        .Where(p => p.AccountId == accountId)
                        .ExecuteUpdateAsync(p => p.SetProperty(x => x.SocialCredits, credit));
                }

                processed += batchIds.Count;
                logger.LogDebug("Processed {Processed}/{Total} accounts", processed, accountIds.Count);
            }

            await cache.RemoveGroupAsync("credits:");
            logger.LogInformation("Social credit profile update completed. Updated {Count} accounts.", accountIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during social credit profile update.");
        }
    }
}
