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
[Route("/api/admin/tests")]
[ApiFeature("admin.tests", Revision = 1)]
public class TestAdminController(AppDatabase db, TestService tests) : ControllerBase
{
    [HttpGet]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<List<SnTest>>> List() => Ok(await db.Tests.Include(x => x.Questions).ThenInclude(x => x.Choices).OrderBy(x => x.Key).ToListAsync());

    [HttpPost]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTest>> Create([FromBody] TestUpsertRequest request)
    {
        var test = new SnTest();
        Apply(test, request);
        if (!Validate(test, out var error)) return BadRequest(error);
        db.Tests.Add(test);
        await db.SaveChangesAsync();
        return Ok(test);
    }

    [HttpPut("{key}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTest>> Update(string key, [FromBody] TestUpsertRequest request)
    {
        var test = await db.Tests.Include(x => x.Questions).ThenInclude(x => x.Choices).FirstOrDefaultAsync(x => x.Key == key);
        if (test is null) return NotFound();
        db.TestQuestions.RemoveRange(test.Questions);
        Apply(test, request);
        if (!Validate(test, out var error)) return BadRequest(error);
        await db.SaveChangesAsync();
        return Ok(test);
    }

    [HttpPost("{key}/publish")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTest>> Publish(string key, [FromQuery] bool published = true)
    {
        var test = await db.Tests.FirstOrDefaultAsync(x => x.Key == key);
        if (test is null) return NotFound();
        test.IsPublished = published;
        await db.SaveChangesAsync();
        return Ok(test);
    }

    [HttpPost("{key}/archive")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTest>> Archive(string key, [FromQuery] bool archived = true)
    {
        var test = await db.Tests.FirstOrDefaultAsync(x => x.Key == key);
        if (test is null) return NotFound();
        test.IsArchived = archived;
        if (archived) test.IsPublished = false;
        await db.SaveChangesAsync();
        return Ok(test);
    }

    [HttpGet("{key}/attempts")]
    [AskPermission(PermissionKeys.TestsReview)]
    public async Task<ActionResult<List<SnTestAttempt>>> ListAttempts(string key, [FromQuery] TestAttemptStatus? status)
    {
        var test = await db.Tests.FirstOrDefaultAsync(x => x.Key == key);
        if (test is null) return NotFound();
        var query = db.TestAttempts.Include(x => x.Answers).Where(x => x.TestId == test.Id);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return Ok(await query.OrderByDescending(x => x.StartedAt).ToListAsync());
    }

    [HttpPost("answers/{answerId:guid}/review")]
    [AskPermission(PermissionKeys.TestsReview)]
    public async Task<ActionResult<SnTestAttempt>> ReviewAnswer(Guid answerId, [FromBody] ReviewTestAnswerRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount reviewer) return Unauthorized();
        var answer = await db.TestAnswers.FirstOrDefaultAsync(x => x.Id == answerId);
        if (answer is null) return NotFound();
        try { return Ok(await tests.ReviewAnswer(reviewer.Id, answer, request.IsCorrect, request.AwardedPoints, request.Note, HttpContext.RequestAborted)); }
        catch (InvalidOperationException ex) { return BadRequest(new ApiError { Code = "PASSPORT_TEST_REVIEW_FAILED", Message = ex.Message, Status = 400 }); }
    }

    private static void Apply(SnTest test, TestUpsertRequest request)
    {
        test.Key = request.Key.Trim(); test.Title = request.Title; test.Description = request.Description; test.IsPublished = request.IsPublished;
        test.PassingScore = request.PassingScore; test.MaxAttempts = request.MaxAttempts; test.AttemptPeriodDays = request.AttemptPeriodDays; test.TimeLimitSeconds = request.TimeLimitSeconds; test.GrantedPermissionGroupKey = string.IsNullOrWhiteSpace(request.GrantedPermissionGroupKey) ? null : request.GrantedPermissionGroupKey.Trim(); test.Config = request.Config;
        test.Questions = request.Questions.Select(q => new SnTestQuestion
        { SortOrder = q.SortOrder, Content = q.Content, Type = q.Type, GradingMode = q.GradingMode, Difficulty = q.Difficulty, Points = q.Points, Config = q.Config,
            Choices = q.Choices.Select(c => new SnTestChoice { SortOrder = c.SortOrder, Content = c.Content, IsCorrect = c.IsCorrect, Config = c.Config }).ToList() }).ToList();
    }
    private static bool Validate(SnTest test, out string error)
    {
        if (string.IsNullOrWhiteSpace(test.Key) || string.IsNullOrWhiteSpace(test.Title) || test.PassingScore is < 0 or > 100 || test.MaxAttempts is < 1 || test.AttemptPeriodDays < 1 || test.TimeLimitSeconds is < 1) { error = "The test configuration is invalid."; return false; }
        if (test.Questions.Any(q => string.IsNullOrWhiteSpace(q.Content) || q.Points < 0 || (q.GradingMode == TestQuestionGradingMode.Auto && (q.Type == TestQuestionType.FreeText || !q.Choices.Any(c => c.IsCorrect))))) { error = "Auto-graded questions must be choice questions with at least one correct choice."; return false; }
        error = string.Empty; return true;
    }
}

public class TestUpsertRequest { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public bool IsPublished { get; set; } public double PassingScore { get; set; } = 100; public int? MaxAttempts { get; set; } public int AttemptPeriodDays { get; set; } = 365; public int? TimeLimitSeconds { get; set; } public string? GrantedPermissionGroupKey { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); public List<TestQuestionUpsertRequest> Questions { get; set; } = []; }
public class TestQuestionUpsertRequest { public int SortOrder { get; set; } public string Content { get; set; } = null!; public TestQuestionType Type { get; set; } public TestQuestionGradingMode GradingMode { get; set; } public int Difficulty { get; set; } public double Points { get; set; } = 1; public Dictionary<string, object?> Config { get; set; } = new(); public List<TestChoiceUpsertRequest> Choices { get; set; } = []; }
public class TestChoiceUpsertRequest { public int SortOrder { get; set; } public string Content { get; set; } = null!; public bool IsCorrect { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); }
public class ReviewTestAnswerRequest { public bool IsCorrect { get; set; } public double AwardedPoints { get; set; } public string? Note { get; set; } }
