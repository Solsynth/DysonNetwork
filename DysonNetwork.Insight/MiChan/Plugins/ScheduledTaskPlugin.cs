using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using NodaTime;

namespace DysonNetwork.Insight.MiChan.Plugins;

/// <summary>
/// Plugin for managing scheduled tasks and getting current time
/// </summary>
public class ScheduledTaskPlugin(
    IServiceProvider serviceProvider,
    ILogger<ScheduledTaskPlugin> logger)
{
    /// <summary>
    /// Get the current time
    /// </summary>
    [KernelFunction("get_current_time")]
    [Description("Get the current date and time. Use this when you need to know what time it is now for scheduling or time-related decisions.")]
    public string GetCurrentTime()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var timeString = now.ToDateTimeUtc().ToString("yyyy-MM-dd HH:mm:ss UTC");
        logger.LogInformation("Getting current time: {Time}", timeString);
        return $"Current time is: {timeString}";
    }

    /// <summary>
    /// List scheduled tasks for an account
    /// </summary>
    [KernelFunction("list_scheduled_tasks")]
    [Description("List all scheduled tasks for a specific account. Use this to see what tasks are scheduled.")]
    public async Task<string> ListScheduledTasksAsync(
        [Description("The account ID (Guid) to list tasks for")]
        Guid accountId,
        [Description("Optional: Filter by active status (true/false). Leave empty for all.")]
        bool? isActive = null,
        [Description("Optional: Filter by status (pending/running/completed/failed/cancelled). Leave empty for all.")]
        string? status = null,
        [Description("Maximum number of tasks to return (default: 20)")]
        int limit = 20)
    {
        using var scope = serviceProvider.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<ScheduledTaskService>();

        try
        {
            logger.LogInformation("Listing scheduled tasks for account {AccountId}", accountId);

            var tasks = await taskService.GetByAccountAsync(
                accountId: accountId,
                skip: 0,
                take: limit,
                isActive: isActive,
                status: status
            );

            if (tasks.Count == 0)
            {
                return "No scheduled tasks found for this account.";
            }

            var results = new StringBuilder();
            results.AppendLine($"Found {tasks.Count} scheduled tasks:");
            results.AppendLine();

            foreach (var task in tasks)
            {
                results.AppendLine($"--- Task {task.Id} ---");
                results.AppendLine($"Status: {task.Status}");
                results.AppendLine($"Scheduled At: {task.ScheduledAt.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");
                results.AppendLine($"Prompt: {task.Prompt.Substring(0, Math.Min(100, task.Prompt.Length))}...");
                
                if (task.RecurrenceInterval.HasValue)
                {
                    results.AppendLine($"Recurrence: Every {FormatDuration(task.RecurrenceInterval.Value)}");
                }
                
                if (task.ExecutionCount > 0)
                {
                    results.AppendLine($"Execution Count: {task.ExecutionCount}");
                }
                
                if (task.LastExecutedAt.HasValue)
                {
                    results.AppendLine($"Last Executed: {task.LastExecutedAt.Value.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");
                }
                
                results.AppendLine();
            }

            return results.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error listing scheduled tasks for account {AccountId}", accountId);
            return $"Error listing tasks: {ex.Message}";
        }
    }

    /// <summary>
    /// Create a new scheduled task
    /// </summary>
    [KernelFunction("create_scheduled_task")]
    [Description("Create a new scheduled task. Use this to schedule something to be done at a specific time in the future. Time should be in UTC format (e.g., '2026-02-15 10:00:00').")]
    public async Task<string> CreateScheduledTaskAsync(
        [Description("The account ID (Guid) that owns this task")]
        Guid accountId,
        [Description("The prompt/instruction for what to do when the task runs")]
        string prompt,
        [Description("When to execute the task (UTC datetime, e.g., '2026-02-15 10:00:00')")]
        string scheduledAt,
        [Description("Optional: Recurrence interval in hours (e.g., 24 for daily). Leave empty for one-time tasks.")]
        double? recurrenceHours = null,
        [Description("Optional: When the recurring task should end (UTC datetime). Leave empty for no end date.")]
        string? recurrenceEndAt = null,
        [Description("Optional: Additional context or notes for the task")]
        string? context = null)
    {
        using var scope = serviceProvider.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<ScheduledTaskService>();

        try
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return "Error: Prompt cannot be empty.";

            // Parse the scheduled time
            if (!DateTime.TryParse(scheduledAt, out var scheduledDateTime))
            {
                return "Error: Invalid scheduled time format. Please use format like '2026-02-15 10:00:00'";
            }

            var scheduledInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(scheduledDateTime, DateTimeKind.Utc));

            // Parse recurrence end if provided
            Instant? recurrenceEndInstant = null;
            if (!string.IsNullOrEmpty(recurrenceEndAt))
            {
                if (DateTime.TryParse(recurrenceEndAt, out var endDateTime))
                {
                    recurrenceEndInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(endDateTime, DateTimeKind.Utc));
                }
                else
                {
                    return "Error: Invalid recurrence end time format. Please use format like '2026-02-15 10:00:00'";
                }
            }

            // Parse recurrence interval
            Duration? recurrenceInterval = recurrenceHours.HasValue 
                ? Duration.FromHours(recurrenceHours.Value) 
                : null;

            logger.LogInformation("Creating scheduled task for account {AccountId} at {ScheduledAt}", 
                accountId, scheduledInstant);

            var task = await taskService.CreateAsync(
                accountId: accountId,
                prompt: prompt,
                scheduledAt: scheduledInstant,
                recurrenceInterval: recurrenceInterval,
                recurrenceEndAt: recurrenceEndInstant,
                context: context
            );

            var response = new StringBuilder();
            response.AppendLine($"Task created successfully with ID: {task.Id}");
            response.AppendLine($"Scheduled for: {task.ScheduledAt.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");
            
            if (task.RecurrenceInterval.HasValue)
            {
                response.AppendLine($"Recurrence: Every {FormatDuration(task.RecurrenceInterval.Value)}");
            }

            logger.LogInformation("Successfully created task {TaskId}", task.Id);

            return response.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating scheduled task");
            return $"Error creating task: {ex.Message}";
        }
    }

    /// <summary>
    /// Get details of a specific scheduled task
    /// </summary>
    [KernelFunction("get_scheduled_task")]
    [Description("Get detailed information about a specific scheduled task by its ID.")]
    public async Task<string> GetScheduledTaskAsync(
        [Description("The ID of the task to retrieve")]
        string taskId)
    {
        using var scope = serviceProvider.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<ScheduledTaskService>();

        try
        {
            if (!Guid.TryParse(taskId, out var id))
            {
                return "Error: Invalid task ID format.";
            }

            logger.LogInformation("Getting scheduled task {TaskId}", taskId);

            var task = await taskService.GetAsync(id);

            if (task == null)
            {
                return $"Task with ID {taskId} not found.";
            }

            var result = new StringBuilder();
            result.AppendLine($"Task ID: {task.Id}");
            result.AppendLine($"Account ID: {task.AccountId}");
            result.AppendLine($"Status: {task.Status}");
            result.AppendLine($"Is Active: {task.IsActive}");
            result.AppendLine($"Scheduled At: {task.ScheduledAt.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");
            result.AppendLine($"Prompt: {task.Prompt}");
            
            if (!string.IsNullOrEmpty(task.Context))
            {
                result.AppendLine($"Context: {task.Context}");
            }
            
            if (task.RecurrenceInterval.HasValue)
            {
                result.AppendLine($"Recurrence Interval: Every {FormatDuration(task.RecurrenceInterval.Value)}");
            }
            
            if (task.RecurrenceEndAt.HasValue)
            {
                result.AppendLine($"Recurrence Ends At: {task.RecurrenceEndAt.Value.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");
            }
            
            result.AppendLine($"Execution Count: {task.ExecutionCount}");
            
            if (task.LastExecutedAt.HasValue)
            {
                result.AppendLine($"Last Executed At: {task.LastExecutedAt.Value.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");
            }
            
            if (task.CompletedAt.HasValue)
            {
                result.AppendLine($"Completed At: {task.CompletedAt.Value.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");
            }
            
            if (!string.IsNullOrEmpty(task.ErrorMessage))
            {
                result.AppendLine($"Error Message: {task.ErrorMessage}");
            }
            
            result.AppendLine($"Created At: {task.CreatedAt.ToDateTimeUtc():yyyy-MM-dd HH:mm:ss UTC}");

            return result.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting scheduled task {TaskId}", taskId);
            return $"Error getting task: {ex.Message}";
        }
    }

    /// <summary>
    /// Update an existing scheduled task
    /// </summary>
    [KernelFunction("update_scheduled_task")]
    [Description("Update an existing scheduled task. Only provide the fields you want to change. Can only update tasks that are still pending.")]
    public async Task<string> UpdateScheduledTaskAsync(
        [Description("The ID of the task to update")]
        string taskId,
        [Description("Optional: New prompt/instruction")]
        string? prompt = null,
        [Description("Optional: New scheduled time (UTC datetime, e.g., '2026-02-15 10:00:00')")]
        string? scheduledAt = null,
        [Description("Optional: New recurrence interval in hours (e.g., 24 for daily). Set to 0 to remove recurrence.")]
        double? recurrenceHours = null,
        [Description("Optional: New recurrence end time (UTC datetime). Leave empty to remove end date.")]
        string? recurrenceEndAt = null,
        [Description("Optional: New context/notes")]
        string? context = null)
    {
        using var scope = serviceProvider.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<ScheduledTaskService>();

        try
        {
            if (!Guid.TryParse(taskId, out var id))
            {
                return "Error: Invalid task ID format.";
            }

            // Parse scheduled time if provided
            Instant? scheduledInstant = null;
            if (!string.IsNullOrEmpty(scheduledAt))
            {
                if (DateTime.TryParse(scheduledAt, out var scheduledDateTime))
                {
                    scheduledInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(scheduledDateTime, DateTimeKind.Utc));
                }
                else
                {
                    return "Error: Invalid scheduled time format. Please use format like '2026-02-15 10:00:00'";
                }
            }

            // Parse recurrence end if provided
            Instant? recurrenceEndInstant = null;
            if (!string.IsNullOrEmpty(recurrenceEndAt))
            {
                if (DateTime.TryParse(recurrenceEndAt, out var endDateTime))
                {
                    recurrenceEndInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(endDateTime, DateTimeKind.Utc));
                }
                else
                {
                    return "Error: Invalid recurrence end time format.";
                }
            }

            // Parse recurrence interval (0 means remove recurrence)
            Duration? recurrenceInterval = recurrenceHours.HasValue 
                ? (recurrenceHours.Value > 0 ? Duration.FromHours(recurrenceHours.Value) : null)
                : null;

            logger.LogInformation("Updating scheduled task {TaskId}", taskId);

            var task = await taskService.UpdateAsync(
                id: id,
                prompt: prompt,
                scheduledAt: scheduledInstant,
                recurrenceInterval: recurrenceInterval,
                recurrenceEndAt: recurrenceEndInstant,
                context: context
            );

            if (task == null)
            {
                return $"Task with ID {taskId} not found.";
            }

            return $"Task {taskId} updated successfully.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating scheduled task {TaskId}", taskId);
            return $"Error updating task: {ex.Message}";
        }
    }

    /// <summary>
    /// Cancel a scheduled task
    /// </summary>
    [KernelFunction("cancel_scheduled_task")]
    [Description("Cancel a pending scheduled task. Only tasks with 'pending' status can be cancelled.")]
    public async Task<string> CancelScheduledTaskAsync(
        [Description("The ID of the task to cancel")]
        string taskId)
    {
        using var scope = serviceProvider.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<ScheduledTaskService>();

        try
        {
            if (!Guid.TryParse(taskId, out var id))
            {
                return "Error: Invalid task ID format.";
            }

            logger.LogInformation("Cancelling scheduled task {TaskId}", taskId);

            var success = await taskService.CancelAsync(id);

            if (!success)
            {
                return $"Task with ID {taskId} not found or cannot be cancelled (only pending tasks can be cancelled).";
            }

            return $"Task {taskId} cancelled successfully.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cancelling scheduled task {TaskId}", taskId);
            return $"Error cancelling task: {ex.Message}";
        }
    }

    /// <summary>
    /// Delete a scheduled task
    /// </summary>
    [KernelFunction("delete_scheduled_task")]
    [Description("Delete a scheduled task permanently. This removes the task from the system.")]
    public async Task<string> DeleteScheduledTaskAsync(
        [Description("The ID of the task to delete")]
        string taskId)
    {
        using var scope = serviceProvider.CreateScope();
        var taskService = scope.ServiceProvider.GetRequiredService<ScheduledTaskService>();

        try
        {
            if (!Guid.TryParse(taskId, out var id))
            {
                return "Error: Invalid task ID format.";
            }

            logger.LogInformation("Deleting scheduled task {TaskId}", taskId);

            var success = await taskService.DeleteAsync(id);

            if (!success)
            {
                return $"Task with ID {taskId} not found.";
            }

            return $"Task {taskId} deleted successfully.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting scheduled task {TaskId}", taskId);
            return $"Error deleting task: {ex.Message}";
        }
    }

    private string FormatDuration(Duration duration)
    {
        var parts = new List<string>();
        
        var totalHours = duration.TotalHours;
        var days = (int)(totalHours / 24);
        var hours = (int)(totalHours % 24);
        var minutes = (int)((duration.TotalMinutes % 60));
        
        if (days > 0)
            parts.Add($"{days} day{(days > 1 ? "s" : "")}");
        if (hours > 0)
            parts.Add($"{hours} hour{(hours > 1 ? "s" : "")}");
        if (minutes > 0)
            parts.Add($"{minutes} minute{(minutes > 1 ? "s" : "")}");
            
        return parts.Count > 0 ? string.Join(" ", parts) : "0 minutes";
    }
}
