using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring.Email;

[ApiController]
[Route("/api/admin/email-plans")]
[Authorize]
public class EmailSendingPlanAdminController(EmailSendingPlanService plans) : ControllerBase
{
    public class CreateEmailSendingPlanRequest
    {
        public Guid? AccountId { get; set; }
        public List<Guid>? AccountIds { get; set; }
        public bool BroadcastToAll { get; set; }
        [MaxLength(256)] public string? SendingPlanKey { get; set; }
        [MaxLength(1024)] public string Subject { get; set; } = string.Empty;
        [MaxLength(1_000_000)] public string HtmlBody { get; set; } = string.Empty;
        public Instant? PlannedStartAt { get; set; }
        [Range(1, 1_000_000)] public int MaxEmailsPerInterval { get; set; }
        [Range(1, 1_440)] public int IntervalMinutes { get; set; }
        [Range(1, 1_000_000)] public int? MaxEmailsPerDay { get; set; }
    }

    [HttpPost]
    [AskPermission("emails.send")]
    public async Task<ActionResult<EmailSendingPlanService.EmailSendingPlanView>> CreatePlan(
        [FromBody] CreateEmailSendingPlanRequest request,
        CancellationToken cancellationToken
    )
    {
        if (!request.BroadcastToAll && !request.AccountId.HasValue && request.AccountIds is not { Count: > 0 })
            return BadRequest(new ApiError { Code = "EMAIL_PLAN_TARGETING_REQUIRED", Message = "Provide account_id, account_ids, or set broadcast_to_all=true.", Status = 400 });
        if (string.IsNullOrWhiteSpace(request.Subject))
            return BadRequest(new ApiError { Code = "EMAIL_PLAN_SUBJECT_REQUIRED", Message = "Subject is required.", Status = 400 });
        if (string.IsNullOrWhiteSpace(request.HtmlBody))
            return BadRequest(new ApiError { Code = "EMAIL_PLAN_HTML_BODY_REQUIRED", Message = "Html body is required.", Status = 400 });
        if (request.MaxEmailsPerDay.HasValue && request.MaxEmailsPerDay.Value < request.MaxEmailsPerInterval)
            return BadRequest(new ApiError { Code = "EMAIL_PLAN_MAX_EMAILS_PER_DAY_INVALID", Message = "max_emails_per_day must be greater than or equal to max_emails_per_interval.", Status = 400 });
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Status = 401 });

        try
        {
            var plan = await plans.CreatePlanAsync(
                new EmailSendingPlanService.CreateEmailSendingPlanCommand(
                    request.AccountId,
                    request.AccountIds,
                    request.BroadcastToAll,
                    request.Subject,
                    request.HtmlBody,
                    request.SendingPlanKey,
                    request.PlannedStartAt,
                    request.MaxEmailsPerInterval,
                    request.IntervalMinutes,
                    request.MaxEmailsPerDay
                ),
                Guid.Parse(currentUser.Id),
                cancellationToken
            );
            return Ok(plan);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError { Code = "EMAIL_PLAN_VALIDATION_ERROR", Message = ex.Message, Status = 400 });
        }
        catch (DbUpdateException)
        {
            return Conflict(ApiError.Conflict("The sending plan key already exists.", code: "EMAIL_PLAN_KEY_CONFLICT"));
        }
    }

    [HttpGet]
    [AskPermission("emails.send")]
    public async Task<ActionResult<List<EmailSendingPlanService.EmailSendingPlanView>>> ListPlans(
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0,
        [FromQuery] EmailSendingPlanStatus? status = null,
        CancellationToken cancellationToken = default
    )
    {
        take = Math.Clamp(take, 1, 100);
        offset = Math.Max(0, offset);

        var (items, totalCount) = await plans.ListPlansAsync(offset, take, status, cancellationToken);
        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(items);
    }

    [HttpGet("{planId:guid}")]
    [AskPermission("emails.send")]
    public async Task<ActionResult<EmailSendingPlanService.EmailSendingPlanView>> GetPlan(
        Guid planId,
        CancellationToken cancellationToken
    )
    {
        var plan = await plans.GetPlanAsync(planId, cancellationToken: cancellationToken);
        return plan is null
            ? NotFound(new ApiError { Code = "EMAIL_PLAN_NOT_FOUND", Message = "The requested email sending plan was not found.", Status = 404 })
            : Ok(plan);
    }

    [HttpPost("{planId:guid}/pause")]
    [AskPermission("emails.send")]
    public async Task<ActionResult<EmailSendingPlanService.EmailSendingPlanView>> PausePlan(
        Guid planId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(await plans.PausePlanAsync(planId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "EMAIL_PLAN_NOT_FOUND", Message = "The requested email sending plan was not found.", Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiError.Conflict(ex.Message, code: "EMAIL_PLAN_PAUSE_CONFLICT"));
        }
    }

    [HttpPost("{planId:guid}/resume")]
    [AskPermission("emails.send")]
    public async Task<ActionResult<EmailSendingPlanService.EmailSendingPlanView>> ResumePlan(
        Guid planId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(await plans.ResumePlanAsync(planId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "EMAIL_PLAN_NOT_FOUND", Message = "The requested email sending plan was not found.", Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiError.Conflict(ex.Message, code: "EMAIL_PLAN_RESUME_CONFLICT"));
        }
    }

    [HttpPost("{planId:guid}/advance")]
    [AskPermission("emails.send")]
    public async Task<ActionResult<EmailSendingPlanService.EmailSendingPlanView>> AdvancePlan(
        Guid planId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return Ok(await plans.AdvancePlanIntervalAsync(planId, isManual: true, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ApiError { Code = "EMAIL_PLAN_NOT_FOUND", Message = "The requested email sending plan was not found.", Status = 404 });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiError.Conflict(ex.Message, code: "EMAIL_PLAN_ADVANCE_CONFLICT"));
        }
    }
}
