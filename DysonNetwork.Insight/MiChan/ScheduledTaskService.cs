using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Insight.MiChan;

public class ScheduledTaskService(
    AppDatabase database,
    ILogger<ScheduledTaskService> logger
)
{
    public async Task<MiChanScheduledTask> CreateAsync(
        Guid accountId,
        string prompt,
        Instant scheduledAt,
        Duration? recurrenceInterval = null,
        Instant? recurrenceEndAt = null,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        var task = new MiChanScheduledTask
        {
            AccountId = accountId,
            Prompt = prompt,
            ScheduledAt = scheduledAt,
            RecurrenceInterval = recurrenceInterval,
            RecurrenceEndAt = recurrenceEndAt,
            Context = context,
            Status = "pending",
            IsActive = true
        };

        database.ScheduledTasks.Add(task);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Created scheduled task {TaskId} for account {AccountId}, scheduled at {ScheduledAt}",
            task.Id, accountId, scheduledAt);

        return task;
    }

    public async Task<MiChanScheduledTask?> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await database.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<List<MiChanScheduledTask>> GetByAccountAsync(
        Guid accountId,
        int skip = 0,
        int take = 50,
        bool? isActive = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = database.ScheduledTasks
            .Where(t => t.AccountId == accountId);

        if (isActive.HasValue)
            query = query.Where(t => t.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(status))
            query = query.Where(t => t.Status == status);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<MiChanScheduledTask>> GetPendingTasksAsync(
        CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        return await database.ScheduledTasks
            .Where(t => t.IsActive && t.Status == "pending" && t.ScheduledAt <= now)
            .OrderBy(t => t.ScheduledAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<MiChanScheduledTask?> UpdateAsync(
        Guid id,
        string? prompt = null,
        Instant? scheduledAt = null,
        Duration? recurrenceInterval = null,
        Instant? recurrenceEndAt = null,
        string? context = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var task = await database.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (task == null)
        {
            logger.LogWarning("Cannot update non-existent scheduled task {TaskId}", id);
            return null;
        }

        if (prompt != null)
            task.Prompt = prompt;
        if (scheduledAt.HasValue)
            task.ScheduledAt = scheduledAt.Value;
        if (recurrenceInterval.HasValue)
            task.RecurrenceInterval = recurrenceInterval;
        if (recurrenceEndAt.HasValue)
            task.RecurrenceEndAt = recurrenceEndAt;
        if (context != null)
            task.Context = context;
        if (isActive.HasValue)
            task.IsActive = isActive.Value;

        await database.SaveChangesAsync(cancellationToken);

        logger.LogDebug("Updated scheduled task {TaskId}", id);
        return task;
    }

    public async Task<bool> CancelAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var task = await database.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (task == null)
        {
            logger.LogWarning("Cannot cancel non-existent scheduled task {TaskId}", id);
            return false;
        }

        if (task.Status != "pending")
        {
            logger.LogWarning("Cannot cancel task {TaskId} with status {Status}", id, task.Status);
            return false;
        }

        task.Status = "cancelled";
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Cancelled scheduled task {TaskId}", id);
        return true;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var task = await database.ScheduledTasks
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (task == null)
        {
            logger.LogWarning("Cannot delete non-existent scheduled task {TaskId}", id);
            return false;
        }

        task.IsActive = false;
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Deleted scheduled task {TaskId}", id);
        return true;
    }

    public async Task MarkAsRunningAsync(
        MiChanScheduledTask task,
        CancellationToken cancellationToken = default)
    {
        task.Status = "running";
        await database.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAsCompletedAsync(
        MiChanScheduledTask task,
        CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        if (task.RecurrenceInterval.HasValue)
        {
            var nextRun = now + task.RecurrenceInterval.Value;

            if (task.RecurrenceEndAt.HasValue && nextRun > task.RecurrenceEndAt.Value)
            {
                task.Status = "completed";
                task.CompletedAt = now;
                task.IsActive = false;
            }
            else
            {
                task.ScheduledAt = nextRun;
                task.Status = "pending";
            }
        }
        else
        {
            task.Status = "completed";
            task.CompletedAt = now;
            task.IsActive = false;
        }

        task.LastExecutedAt = now;
        task.ExecutionCount++;

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Marked task {TaskId} as completed. ExecutionCount: {Count}, NextStatus: {Status}",
            task.Id, task.ExecutionCount, task.Status);
    }

    public async Task MarkAsFailedAsync(
        MiChanScheduledTask task,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var now = SystemClock.Instance.GetCurrentInstant();

        task.Status = "failed";
        task.ErrorMessage = errorMessage;
        task.LastExecutedAt = now;
        task.ExecutionCount++;

        await database.SaveChangesAsync(cancellationToken);

        logger.LogError("Marked task {TaskId} as failed: {Error}", task.Id, errorMessage);
    }
}
