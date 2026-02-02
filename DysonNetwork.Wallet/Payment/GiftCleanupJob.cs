using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Quartz;

namespace DysonNetwork.Wallet.Payment;

public class GiftCleanupJob(
    AppDatabase db,
    ILogger<GiftCleanupJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Starting gift cleanup job...");

        var now = SystemClock.Instance.GetCurrentInstant();
        // Clean up gifts that are in Created status and older than 24 hours
        var cutoffTime = now.Minus(Duration.FromHours(24));

        var oldCreatedGifts = await db.WalletGifts
            .Where(g => g.Status == GiftStatus.Created)
            .Where(g => g.CreatedAt < cutoffTime)
            .ToListAsync();

        if (oldCreatedGifts.Count == 0)
        {
            logger.LogInformation("No old created gifts to clean up");
            return;
        }

        logger.LogInformation("Found {Count} old created gifts to clean up", oldCreatedGifts.Count);

        // Remove the gifts
        db.WalletGifts.RemoveRange(oldCreatedGifts);
        await db.SaveChangesAsync();

        logger.LogInformation("Successfully cleaned up {Count} old created gifts", oldCreatedGifts.Count);
    }
}
