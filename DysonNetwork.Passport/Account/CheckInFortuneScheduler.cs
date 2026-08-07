using DysonNetwork.Shared.Models;

namespace DysonNetwork.Passport.Account;

public class CheckInFortuneScheduler(
    IServiceScopeFactory scopeFactory,
    ILogger<CheckInFortuneScheduler> logger
)
{
    public void Schedule(Guid checkInResultId, SnAccount account)
    {
        _ = Task.Run(
            async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var accountEvents = scope.ServiceProvider.GetRequiredService<AccountEventService>();
                    await accountEvents.GenerateCheckInFortuneAsync(checkInResultId, account);
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Failed to generate check-in fortune for result {CheckInResultId}",
                        checkInResultId
                    );
                }
            },
            CancellationToken.None
        );
    }
}
