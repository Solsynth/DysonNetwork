using Quartz;

namespace DysonNetwork.Ring.Email;

public class EmailSendingPlanAdvanceJob(
    IServiceScopeFactory scopeFactory,
    ILogger<EmailSendingPlanAdvanceJob> logger
) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EmailSendingPlanService>();

        try
        {
            await service.AdvanceDuePlansAsync(context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to advance due email sending plans.");
            throw;
        }
    }
}
