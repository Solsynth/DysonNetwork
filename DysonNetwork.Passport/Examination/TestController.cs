using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Examination;

[Authorize]
[ApiController]
[Route("/api/tests")]
[ApiFeature("tests", Revision = 1)]
public class TestController(AppDatabase db, TestService tests) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<ParticipantTest>>> ListPublicTests()
    {
        var items = await db.Tests.Include(x => x.Questions).ThenInclude(x => x.Choices)
            .Where(x => x.IsPublished && x.IsListed && !x.IsArchived).OrderBy(x => x.Title).ToListAsync();
        return Ok(items.Select(ToParticipantTest));
    }
    [HttpGet("activation")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ActivationRequirementState>> GetActivationRequirements()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount user) return Unauthorized();
        return Ok(await tests.GetActivationRequirements(user.Id, HttpContext.RequestAborted));
    }

    [HttpPost("activation/recheck")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ActivationRequirementState>> RecheckActivationRequirements()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount user) return Unauthorized();
        await tests.TryActivateAccount(user.Id, HttpContext.RequestAborted);
        return Ok(await tests.GetActivationRequirements(user.Id, HttpContext.RequestAborted));
    }

    [HttpGet("{key}")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ParticipantTest>> GetTest(string key)
    {
        var test = await LoadPublishedTest(key);
        return test is null ? NotFound() : Ok(ToParticipantTest(test));
    }

    [HttpPost("{key}/attempts")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ParticipantAttempt>> StartAttempt(string key)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount user) return Unauthorized();
        var test = await LoadPublishedTest(key);
        if (test is null) return NotFound();
        try { return Ok(ToParticipantAttempt(await tests.StartAttempt(user.Id, test, HttpContext.RequestAborted))); }
        catch (InvalidOperationException ex) { return BadRequest(new ApiError { Code = "PASSPORT_TEST_ATTEMPT_UNAVAILABLE", Message = ex.Message, Status = 400 }); }
    }

    [HttpPost("attempts/{attemptId:guid}/submit")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ParticipantAttempt>> SubmitAttempt(Guid attemptId, [FromBody] SubmitTestAttemptRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount user) return Unauthorized();
        var attempt = await db.TestAttempts.Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == attemptId);
        if (attempt is null) return NotFound();
        try { return Ok(ToParticipantAttempt(await tests.SubmitAttempt(user.Id, attempt, request.Answers, HttpContext.RequestAborted))); }
        catch (InvalidOperationException ex) { return BadRequest(new ApiError { Code = "PASSPORT_TEST_SUBMIT_FAILED", Message = ex.Message, Status = 400 }); }
    }

    [HttpGet("attempts/{attemptId:guid}")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ParticipantAttempt>> GetAttempt(Guid attemptId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount user) return Unauthorized();
        var attempt = await db.TestAttempts.Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == attemptId && x.AccountId == user.Id);
        return attempt is null ? NotFound() : Ok(ToParticipantAttempt(attempt));
    }

    private Task<SnTest?> LoadPublishedTest(string key) => db.Tests.Include(x => x.Questions).ThenInclude(x => x.Choices)
        .FirstOrDefaultAsync(x => x.Key == key && x.IsPublished && !x.IsArchived);

    internal static ParticipantTest ToParticipantTest(SnTest test) => new()
    {
        Key = test.Key, Title = test.Title, Description = test.Description, TimeLimitSeconds = test.TimeLimitSeconds,
        Questions = test.Questions.OrderBy(x => x.SortOrder).Select(q => new ParticipantQuestion
        { Id = q.Id, Content = q.Content, Type = q.Type, Config = q.Config, Choices = q.Choices.OrderBy(c => c.SortOrder).Select(c => new ParticipantChoice { Id = c.Id, Content = c.Content, Config = c.Config }).ToList() }).ToList()
    };
    internal static ParticipantAttempt ToParticipantAttempt(SnTestAttempt x) => new() { Id = x.Id, Status = x.Status, StartedAt = x.StartedAt, DeadlineAt = x.DeadlineAt, SubmittedAt = x.SubmittedAt, ReviewedAt = x.ReviewedAt, Score = x.Score, Answers = x.Answers.Select(a => new ParticipantAnswer { QuestionId = a.QuestionId, Value = a.Value, ReviewNote = a.ReviewNote }).ToList() };
}

public class SubmitTestAttemptRequest { public List<TestAnswerInput> Answers { get; set; } = []; }
public class ParticipantTest { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public int? TimeLimitSeconds { get; set; } public List<ParticipantQuestion> Questions { get; set; } = []; }
public class ParticipantQuestion { public Guid Id { get; set; } public string Content { get; set; } = null!; public TestQuestionType Type { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); public List<ParticipantChoice> Choices { get; set; } = []; }
public class ParticipantChoice { public Guid Id { get; set; } public string Content { get; set; } = null!; public Dictionary<string, object?> Config { get; set; } = new(); }
public class ParticipantAttempt { public Guid Id { get; set; } public TestAttemptStatus Status { get; set; } public NodaTime.Instant StartedAt { get; set; } public NodaTime.Instant? DeadlineAt { get; set; } public NodaTime.Instant? SubmittedAt { get; set; } public NodaTime.Instant? ReviewedAt { get; set; } public double? Score { get; set; } public List<ParticipantAnswer> Answers { get; set; } = []; }
public class ParticipantAnswer { public Guid QuestionId { get; set; } public Dictionary<string, object?> Value { get; set; } = new(); public string? ReviewNote { get; set; } }
