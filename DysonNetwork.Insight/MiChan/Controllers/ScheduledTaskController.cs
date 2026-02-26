#pragma warning disable SKEXP0050

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Mvc;
using NodaTime;

namespace DysonNetwork.Insight.MiChan.Controllers;

[ApiController]
[Route("/api/tasks")]
public class ScheduledTaskController(
    ScheduledTaskService taskService,
    IServiceProvider serviceProvider,
    ILogger<ScheduledTaskController> logger
) : ControllerBase
{
    public class CreateTaskRequest
    {
        [Required] public string Prompt { get; set; } = null!;
        [Required] public Instant ScheduledAt { get; set; }
        public Duration? RecurrenceInterval { get; set; }
        public Instant? RecurrenceEndAt { get; set; }
        public string? Context { get; set; }
    }

    public class UpdateTaskRequest
    {
        public string? Prompt { get; set; }
        public Instant? ScheduledAt { get; set; }
        public Duration? RecurrenceInterval { get; set; }
        public Instant? RecurrenceEndAt { get; set; }
        public string? Context { get; set; }
        public bool? IsActive { get; set; }
    }

    public class ListTasksResponse
    {
        public Guid Id { get; set; }
        public Instant ScheduledAt { get; set; }
        public Duration? RecurrenceInterval { get; set; }
        public Instant? RecurrenceEndAt { get; set; }
        public string Prompt { get; set; } = null!;
        public string? Context { get; set; }
        public string Status { get; set; } = null!;
        public Instant? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int ExecutionCount { get; set; }
        public bool IsActive { get; set; }
        public Instant? LastExecutedAt { get; set; }
        public Instant CreatedAt { get; set; }
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ListTasksResponse>>> ListTasks(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? status = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var tasks = await taskService.GetByAccountAsync(
            accountId, 
            offset, 
            take, 
            isActive, 
            status
        );

        var response = tasks.Select(t => new ListTasksResponse
        {
            Id = t.Id,
            ScheduledAt = t.ScheduledAt,
            RecurrenceInterval = t.RecurrenceInterval,
            RecurrenceEndAt = t.RecurrenceEndAt,
            Prompt = t.Prompt,
            Context = t.Context,
            Status = t.Status,
            CompletedAt = t.CompletedAt,
            ErrorMessage = t.ErrorMessage,
            ExecutionCount = t.ExecutionCount,
            IsActive = t.IsActive,
            LastExecutedAt = t.LastExecutedAt,
            CreatedAt = t.CreatedAt
        }).ToList();

        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MiChanScheduledTask>> CreateTask([FromBody] CreateTaskRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var task = await taskService.CreateAsync(
            accountId,
            request.Prompt,
            request.ScheduledAt,
            request.RecurrenceInterval,
            request.RecurrenceEndAt,
            request.Context
        );

        return Created($"/api/michan/scheduled-tasks/{task.Id}", task);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ListTasksResponse>> GetTask(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var task = await taskService.GetAsync(id);
        if (task == null) return NotFound();

        if (task.AccountId != accountId && !currentUser.IsSuperuser)
            return Forbid();

        var response = new ListTasksResponse
        {
            Id = task.Id,
            ScheduledAt = task.ScheduledAt,
            RecurrenceInterval = task.RecurrenceInterval,
            RecurrenceEndAt = task.RecurrenceEndAt,
            Prompt = task.Prompt,
            Context = task.Context,
            Status = task.Status,
            CompletedAt = task.CompletedAt,
            ErrorMessage = task.ErrorMessage,
            ExecutionCount = task.ExecutionCount,
            IsActive = task.IsActive,
            LastExecutedAt = task.LastExecutedAt,
            CreatedAt = task.CreatedAt
        };

        return Ok(response);
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MiChanScheduledTask>> UpdateTask(Guid id, [FromBody] UpdateTaskRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var task = await taskService.GetAsync(id);
        if (task == null) return NotFound();

        if (task.AccountId != accountId && !currentUser.IsSuperuser)
            return Forbid();

        var updated = await taskService.UpdateAsync(
            id,
            request.Prompt,
            request.ScheduledAt,
            request.RecurrenceInterval,
            request.RecurrenceEndAt,
            request.Context,
            request.IsActive
        );

        if (updated == null) return NotFound();

        return Ok(updated);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> CancelTask(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var task = await taskService.GetAsync(id);
        if (task == null) return NotFound();

        if (task.AccountId != accountId && !currentUser.IsSuperuser)
            return Forbid();

        var success = await taskService.CancelAsync(id);
        if (!success)
            return BadRequest(new { error = "Task cannot be cancelled. Only pending tasks can be cancelled." });

        return Ok(new { success = true, message = "Task cancelled successfully" });
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> DeleteTask(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var task = await taskService.GetAsync(id);
        if (task == null) return NotFound();

        if (task.AccountId != accountId && !currentUser.IsSuperuser)
            return Forbid();

        var success = await taskService.DeleteAsync(id);
        if (!success) return NotFound();

        return Ok(new { success = true, message = "Task deleted successfully" });
    }

    /// <summary>
    /// Admin endpoint to execute a scheduled task immediately, regardless of its scheduled time.
    /// </summary>
    [HttpPost("{id:guid}/run")]
    [AskPermission("michan.admin")]
    [Experimental("SKEXP0050")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> RunTaskImmediately(Guid id)
    {
        var task = await taskService.GetAsync(id);
        if (task == null) return NotFound();

        if (task.Status == "running")
            return BadRequest(new { error = "Task is already running" });

        if (!task.IsActive)
            return BadRequest(new { error = "Task is not active" });

        logger.LogInformation("Admin triggered immediate execution of task {TaskId}", id);

        try
        {
            var job = serviceProvider.GetRequiredService<ScheduledTaskJob>();
            await job.ExecuteTaskDirectlyAsync(task, CancellationToken.None);

            return Ok(new 
            { 
                success = true, 
                message = "Task executed successfully",
                taskId = id,
                status = task.Status,
                executionCount = task.ExecutionCount
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to execute task {TaskId} via admin endpoint", id);
            return BadRequest(new { error = $"Task execution failed: {ex.Message}" });
        }
    }
}

#pragma warning restore SKEXP0050
