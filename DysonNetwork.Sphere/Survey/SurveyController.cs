using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using SurveyQuestionType = DysonNetwork.Shared.Models.SurveyQuestionType;

namespace DysonNetwork.Sphere.Survey;

[ApiController]
[Route("/api/surveys")]
public class SurveyController(
    AppDatabase db,
    SurveyService surveys,
    Publisher.PublisherService pub,
    RemoteAccountService remoteAccountsHelper,
    RemoteActionLogService als
) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SurveyWithStats>> GetSurvey(Guid id)
    {
        var survey = await db.Surveys
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (survey is null) return NotFound("Survey not found");
        var surveyWithAnswer = SurveyWithStats.FromSurvey(survey);

        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Ok(surveyWithAnswer);

        var accountId = Guid.Parse(currentUser.Id);
        var answer = await surveys.GetSurveyAnswer(id, accountId);
        if (answer is not null)
            surveyWithAnswer.UserAnswer = answer;
        surveyWithAnswer.Stats = await surveys.GetSurveyStats(id);

        return Ok(surveyWithAnswer);
    }

    public class SurveyAnswerRequest
    {
        public required Dictionary<string, JsonElement> Answer { get; set; }
    }

    [HttpPost("{id:guid}/answer")]
    [Authorize]
    public async Task<ActionResult<SnSurveyAnswer>> AnswerSurvey(Guid id, [FromBody] SurveyAnswerRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        try
        {
            var result = await surveys.AnswerSurvey(id, accountId, request.Answer);

            als.CreateActionLog(
                accountId,
                "surveys.answer",
                new Dictionary<string, object>
                {
                    { "survey_id", id.ToString() }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return result;
        }
        catch (SurveyValidationException ex)
        {
            return UnprocessableEntity(ApiError.Validation(ex.FieldErrors));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError { Code = "INVALID_STATE", Message = ex.Message, Status = 409 });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "SERVER_ERROR", Message = ex.Message, Status = 400 });
        }
    }

    [HttpDelete("{id:guid}/answer")]
    [Authorize]
    public async Task<IActionResult> DeleteSurveyAnswer(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        try
        {
            await surveys.UnAnswerSurvey(id, accountId);

            als.CreateActionLog(
                accountId,
                "surveys.answer.delete",
                new Dictionary<string, object>
                {
                    { "survey_id", id.ToString() }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return NoContent();
        }
        catch (SurveyValidationException ex)
        {
            return UnprocessableEntity(ApiError.Validation(ex.FieldErrors));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError { Code = "INVALID_STATE", Message = ex.Message, Status = 409 });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "SERVER_ERROR", Message = ex.Message, Status = 400 });
        }
    }

    [HttpGet("{id:guid}/feedback")]
    public async Task<ActionResult<List<SnSurveyAnswer>>> GetSurveyFeedback(
        Guid id,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Cap pagination to prevent unbounded result sets.
        take = Math.Clamp(take, 1, 100);
        offset = Math.Max(offset, 0);

        var survey = await db.Surveys
            .FirstOrDefaultAsync(p => p.Id == id);
        if (survey is null) return NotFound("Survey not found");

        if (!await pub.IsMemberWithRole(survey.PublisherId, accountId, PublisherMemberRole.Viewer))
            return StatusCode(403, "You need to be a viewer to view this survey's feedback.");

        var answerQuery = db.SurveyAnswers
            .Where(a => a.SurveyId == id)
            .AsQueryable();

        var total = await answerQuery.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var answers = await answerQuery
            .OrderByDescending(a => a.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        if (!survey.IsAnonymous)
        {
            var answeredAccountsId = answers.Select(x => x.AccountId).Distinct().ToList();
            var answeredAccounts = await remoteAccountsHelper.GetAccountBatch(answeredAccountsId);

            // Populate Account field for each answer
            foreach (var answer in answers)
            {
                var protoValue = answeredAccounts.FirstOrDefault(a => a.Id == answer.AccountId.ToString());
                if (protoValue is not null)
                    answer.Account = SnAccount.FromProtoValue(protoValue);
            }
        }

        return Ok(answers);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<List<SnSurvey>>> ListSurveys(
        [FromQuery(Name = "pub")] string? pubName,
        [FromQuery] bool active = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20
    )
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        List<Guid> publishers;
        if (pubName is null) publishers = (await pub.GetUserPublishers(accountId)).Select(p => p.Id).ToList();
        else
        {
            publishers = await db.PublisherMembers
                .Include(p => p.Publisher)
                .Where(p => p.Publisher.Name == pubName && p.AccountId == accountId)
                .Select(p => p.PublisherId)
                .ToListAsync();
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var query = db.Surveys
            .Where(e => publishers.Contains(e.PublisherId));
        if (active) query = query.Where(e => !e.EndedAt.HasValue || e.EndedAt > now);

        var totalCount = await query.CountAsync();
        HttpContext.Response.Headers.Append("X-Total", totalCount.ToString());

        var pollsWithStats = await query
            .Skip(offset)
            .Take(take)
            .Include(p => p.Questions)
            .ToListAsync();
        return Ok(pollsWithStats);
    }

    public class SurveyRequest
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public Instant? EndedAt { get; set; }
        public bool? ClearEndedAt { get; set; }
        public bool? IsAnonymous { get; set; }
        public bool? NotifySubscribers { get; set; }
        public List<string>? Attachments { get; set; }
        public List<SurveyRequestQuestion>? Questions { get; set; }
    }

    public class SurveyRequestQuestion
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public SurveyQuestionType Type { get; set; }
        public List<SnSurveyOption>? Options { get; set; }

        [MaxLength(1024)] public string Title { get; set; } = null!;
        [MaxLength(4096)] public string? Description { get; set; }
        public int Order { get; set; } = 0;
        public bool IsRequired { get; set; }

        public int? MaxSelections { get; set; }
        public int? MaxLength { get; set; }
        public double? MinValue { get; set; }
        public double? MaxValue { get; set; }

        public List<string>? Attachments { get; set; }

        private static Guid EnsureId(Guid id) => id == Guid.Empty ? Guid.NewGuid() : id;

        public SnSurveyQuestion ToQuestion() => new()
        {
            Id = EnsureId(Id),
            Type = Type,
            Options = Options?.Select(option => new SnSurveyOption
            {
                Id = EnsureId(option.Id),
                Label = option.Label,
                Description = option.Description,
                Order = option.Order
            }).ToList(),
            Title = Title,
            Description = Description,
            Order = Order,
            IsRequired = IsRequired,
            MaxSelections = MaxSelections,
            MaxLength = MaxLength,
            MinValue = MinValue,
            MaxValue = MaxValue
        };
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnSurvey>> CreateSurvey([FromBody] SurveyRequest request,
        [FromQuery(Name = "pub")] string pubName)
    {
        if (request.Questions is null) return BadRequest("Questions are required.");
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await pub.GetPublisherByName(pubName);
        if (publisher is null) return BadRequest("Publisher was not found.");
        if (!await pub.IsMemberWithRole(publisher.Id, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least be an editor to create surveys as this publisher.");

        var survey = new SnSurvey
        {
            Title = request.Title,
            Description = request.Description,
            EndedAt = request.EndedAt,
            IsAnonymous = request.IsAnonymous ?? false,
            NotifySubscribers = request.NotifySubscribers ?? false,
            PublisherId = publisher.Id,
            Questions = request.Questions.Select(q => q.ToQuestion()).ToList()
        };

        // Resolve attachment IDs into denormalized SnCloudFileReferenceObject snapshots
        // (mirrors PostService.PostAsync). Intro-level + per-question.
        survey.Attachments = await surveys.ResolveAttachmentsAsync(request.Attachments);
        if (request.Questions is not null)
        {
            for (var i = 0; i < request.Questions.Count; i++)
                survey.Questions[i].Attachments =
                    await surveys.ResolveAttachmentsAsync(request.Questions[i].Attachments);
        }

        try
        {
            surveys.ValidateSurvey(survey);
        }
        catch (SurveyValidationException ex)
        {
            return UnprocessableEntity(ApiError.Validation(ex.FieldErrors));
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "SERVER_ERROR", Message = ex.Message, Status = 400 });
        }

        db.Surveys.Add(survey);
        await db.SaveChangesAsync();

        als.CreateActionLog(
            accountId,
            "surveys.create",
            new Dictionary<string, object>
            {
                { "survey_id", survey.Id.ToString() },
                { "title", survey.Title ?? "" },
                { "publisher_id", publisher.Id.ToString() }
            },
            userAgent: Request.Headers.UserAgent,
            ipAddress: Request.GetClientIpAddress()
        );

        return Ok(survey);
    }

    [HttpPatch("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnSurvey>> UpdateSurvey(Guid id, [FromBody] SurveyRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Start a transaction
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var survey = await db.Surveys
                .Include(p => p.Questions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (survey == null) return NotFound("Survey not found");

            // Check if user is an editor of the publisher that owns the survey
            if (!await pub.IsMemberWithRole(survey.PublisherId, accountId, PublisherMemberRole.Editor))
                return StatusCode(403, "You need to be at least an editor to update this survey.");

            // Only Drafts can be edited. Published surveys are immutable (clone to revise),
            // and Archived surveys are fully locked.
            if (survey.Status != SurveyStatus.Draft)
                return Conflict(new ApiError
                {
                    Code = "SURVEY_IMMUTABLE",
                    Message = $"Survey in {survey.Status} status is immutable; clone a new draft to revise.",
                    Status = 409
                });

            // Update properties if they are provided in the request
            if (request.Title != null) survey.Title = request.Title;
            if (request.Description != null) survey.Description = request.Description;
            if (request.ClearEndedAt == true) survey.EndedAt = null;
            else if (request.EndedAt.HasValue) survey.EndedAt = request.EndedAt;
            if (request.IsAnonymous.HasValue) survey.IsAnonymous = request.IsAnonymous.Value;
            if (request.NotifySubscribers.HasValue) survey.NotifySubscribers = request.NotifySubscribers.Value;
            if (request.Attachments is not null)
                survey.Attachments = await surveys.ResolveAttachmentsAsync(request.Attachments);

            db.Update(survey);

            // Update questions if provided
            if (request.Questions != null)
            {
                var incomingQuestions = request.Questions
                    .Select(q => q.ToQuestion())
                    .ToList();
                var incomingQuestionIds = incomingQuestions
                    .Select(q => q.Id)
                    .ToHashSet();

                var existingQuestions = survey.Questions
                    .ToDictionary(q => q.Id);

                foreach (var existingQuestion in survey.Questions.Where(q => !incomingQuestionIds.Contains(q.Id)).ToList())
                    db.SurveyQuestions.Remove(existingQuestion);

                // Walk in parallel with request.Questions so we can resolve attachments per-question.
                for (var i = 0; i < incomingQuestions.Count; i++)
                {
                    var incomingQuestion = incomingQuestions[i];
                    var requestQuestion = request.Questions[i];
                    incomingQuestion.SurveyId = survey.Id;

                    if (existingQuestions.TryGetValue(incomingQuestion.Id, out var existingQuestion))
                    {
                        existingQuestion.Type = incomingQuestion.Type;
                        existingQuestion.Options = incomingQuestion.Options;
                        existingQuestion.Title = incomingQuestion.Title;
                        existingQuestion.Description = incomingQuestion.Description;
                        existingQuestion.Order = incomingQuestion.Order;
                        existingQuestion.IsRequired = incomingQuestion.IsRequired;
                        existingQuestion.MaxSelections = incomingQuestion.MaxSelections;
                        existingQuestion.MaxLength = incomingQuestion.MaxLength;
                        existingQuestion.MinValue = incomingQuestion.MinValue;
                        existingQuestion.MaxValue = incomingQuestion.MaxValue;
                        if (requestQuestion.Attachments is not null)
                            existingQuestion.Attachments =
                                await surveys.ResolveAttachmentsAsync(requestQuestion.Attachments);
                    }
                    else
                    {
                        if (requestQuestion.Attachments is not null)
                            incomingQuestion.Attachments =
                                await surveys.ResolveAttachmentsAsync(requestQuestion.Attachments);
                        survey.Questions.Add(incomingQuestion);
                    }
                }
            }

            surveys.ValidateSurvey(survey);

            await db.SaveChangesAsync();

            // Commit the transaction if all operations succeed
            await transaction.CommitAsync();

            als.CreateActionLog(
                accountId,
                "surveys.update",
                new Dictionary<string, object>
                {
                    { "survey_id", survey.Id.ToString() },
                    { "title", survey.Title ?? "" }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return Ok(survey);
        }
        catch (SurveyValidationException ex)
        {
            await transaction.RollbackAsync();
            return UnprocessableEntity(ApiError.Validation(ex.FieldErrors));
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync();
            return Conflict(new ApiError { Code = "INVALID_STATE", Message = ex.Message, Status = 409 });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return BadRequest(new ApiError { Code = "SERVER_ERROR", Message = ex.Message, Status = 400 });
        }
    }

    // ---- Lifecycle endpoints --------------------------------------------------

    [HttpPost("{id:guid}/publish")]
    [Authorize]
    public async Task<ActionResult<SnSurvey>> PublishSurvey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var existing = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null) return NotFound("Survey not found");
        if (!await pub.IsMemberWithRole(existing.PublisherId, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least editor rights to publish this survey.");

        try
        {
            var result = await surveys.PublishSurveyAsync(id);

            als.CreateActionLog(
                accountId,
                "surveys.publish",
                new Dictionary<string, object> { { "survey_id", id.ToString() } },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return Ok(result);
        }
        catch (SurveyValidationException ex)
        {
            return UnprocessableEntity(ApiError.Validation(ex.FieldErrors));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError { Code = "INVALID_STATE", Message = ex.Message, Status = 409 });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "SERVER_ERROR", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize]
    public async Task<ActionResult<SnSurvey>> ArchiveSurvey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var existing = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null) return NotFound("Survey not found");
        if (!await pub.IsMemberWithRole(existing.PublisherId, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least editor rights to archive this survey.");

        try
        {
            var result = await surveys.ArchiveSurveyAsync(id);

            als.CreateActionLog(
                accountId,
                "surveys.archive",
                new Dictionary<string, object> { { "survey_id", id.ToString() } },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError { Code = "INVALID_STATE", Message = ex.Message, Status = 409 });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "SERVER_ERROR", Message = ex.Message, Status = 400 });
        }
    }

    [HttpPost("{id:guid}/clone")]
    [Authorize]
    public async Task<ActionResult<SnSurvey>> CloneSurvey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var existing = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (existing is null) return NotFound("Survey not found");
        if (!await pub.IsMemberWithRole(existing.PublisherId, accountId, PublisherMemberRole.Editor))
            return StatusCode(403, "You need at least editor rights to clone this survey.");

        try
        {
            var result = await surveys.CloneSurveyAsync(id);

            als.CreateActionLog(
                accountId,
                "surveys.clone",
                new Dictionary<string, object>
                {
                    { "source_survey_id", id.ToString() },
                    { "new_survey_id", result.Id.ToString() }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError { Code = "INVALID_STATE", Message = ex.Message, Status = 409 });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiError { Code = "SERVER_ERROR", Message = ex.Message, Status = 400 });
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> DeleteSurvey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        // Start a transaction
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var survey = await db.Surveys
                .Include(p => p.Questions)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (survey == null) return NotFound("Survey not found");

            // Check if user is an editor of the publisher that owns the survey
            if (!await pub.IsMemberWithRole(survey.PublisherId, accountId, PublisherMemberRole.Editor))
                return StatusCode(403, "You need to be at least an editor to delete this survey.");

            // Delete all answers for this survey
            var answers = await db.SurveyAnswers
                .Where(a => a.SurveyId == id)
                .ToListAsync();

            if (answers.Count != 0)
                db.SurveyAnswers.RemoveRange(answers);

            // Delete all questions for this survey
            if (survey.Questions.Count != 0)
                db.SurveyQuestions.RemoveRange(survey.Questions);

            // Store survey details for logging before deletion
            var surveyId = survey.Id;
            var surveyTitle = survey.Title;
            var publisherId = survey.PublisherId;

            // Finally, delete the survey itself
            db.Surveys.Remove(survey);

            await db.SaveChangesAsync();

            als.CreateActionLog(
                accountId,
                "surveys.delete",
                new Dictionary<string, object>
                {
                    { "survey_id", surveyId.ToString() },
                    { "title", surveyTitle ?? "" },
                    { "publisher_id", publisherId.ToString() }
                },
                userAgent: Request.Headers.UserAgent,
                ipAddress: Request.GetClientIpAddress()
            );

            // Commit the transaction if all operations succeed
            await transaction.CommitAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, "An error occurred while deleting the survey... " + ex.Message);
        }
    }

    // ---- Subscription endpoints ----------------------------------------------
    //
    // Per-survey subscriptions mirror SnPostSubscription. A subscriber receives a
    // push notification (topic "surveys.answer") when someone answers the survey,
    // as long as the survey's NotifySubscribers flag is set. A user can have at
    // most one active subscription per survey; re-subscribing is idempotent.

    [HttpPost("{id:guid}/subscribe")]
    [Authorize]
    public async Task<ActionResult<SnSurveySubscription>> SubscribeToSurvey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var survey = await db.Surveys.FirstOrDefaultAsync(p => p.Id == id);
        if (survey is null) return NotFound("Survey not found");

        var existing = await db.SurveySubscriptions
            .FirstOrDefaultAsync(s => s.SurveyId == id && s.AccountId == accountId);
        if (existing is not null)
            return Ok(existing);

        var subscription = new SnSurveySubscription
        {
            Id = Guid.NewGuid(),
            SurveyId = id,
            AccountId = accountId
        };
        db.SurveySubscriptions.Add(subscription);
        await db.SaveChangesAsync();

        return Ok(subscription);
    }

    [HttpPost("{id:guid}/unsubscribe")]
    [Authorize]
    public async Task<IActionResult> UnsubscribeFromSurvey(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var subscription = await db.SurveySubscriptions
            .FirstOrDefaultAsync(s => s.SurveyId == id && s.AccountId == accountId);
        if (subscription is null) return NoContent();

        db.SurveySubscriptions.Remove(subscription);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:guid}/subscription")]
    [Authorize]
    public async Task<ActionResult<SnSurveySubscription>> GetSurveySubscription(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var survey = await db.Surveys.FirstOrDefaultAsync(p => p.Id == id);
        if (survey is null) return NotFound("Survey not found");

        var subscription = await db.SurveySubscriptions
            .FirstOrDefaultAsync(s => s.SurveyId == id && s.AccountId == accountId);
        if (subscription is null) return NotFound("Subscription not found");

        return Ok(subscription);
    }
}
