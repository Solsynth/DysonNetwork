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
[ApiFeature("admin.tests", Revision = 2)]
public class TestAdminController(AppDatabase db, TestService tests) : ControllerBase
{
    [HttpGet]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<List<SnTest>>> List() => Ok(await TestsQuery().OrderBy(x => x.Key).ToListAsync());

    [HttpPost]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTest>> Create([FromBody] TestUpsertRequest request)
    {
        if (!Validate(request, out var error)) return BadRequest(error);
        var groups = await ResolveGroups(request.QuestionGroups);
        if (groups is null) return BadRequest("One or more question groups do not exist.");
        var test = new SnTest();
        Apply(test, request, groups);
        db.Tests.Add(test);
        await db.SaveChangesAsync();
        return Ok(await TestsQuery().FirstAsync(x => x.Id == test.Id));
    }

    [HttpPut("{key}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTest>> Update(string key, [FromBody] TestUpsertRequest request)
    {
        var test = await TestsQuery().FirstOrDefaultAsync(x => x.Key == key);
        if (test is null) return NotFound();
        if (!Validate(request, out var error)) return BadRequest(error);
        var groups = await ResolveGroups(request.QuestionGroups);
        if (groups is null) return BadRequest("One or more question groups do not exist.");
        db.TestQuestionGroupAssignments.RemoveRange(test.QuestionGroups);
        Apply(test, request, groups);
        await db.SaveChangesAsync();
        return Ok(await TestsQuery().FirstAsync(x => x.Id == test.Id));
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

    private IQueryable<SnTest> TestsQuery() => db.Tests.Include(x => x.QuestionGroups).ThenInclude(x => x.QuestionGroup).ThenInclude(x => x.Questions).ThenInclude(x => x.Choices);

    private async Task<Dictionary<string, SnTestQuestionGroup>?> ResolveGroups(List<TestQuestionGroupAssignmentUpsertRequest> assignments)
    {
        var keys = assignments.Select(x => x.QuestionGroupKey.Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var groups = await db.TestQuestionGroups.Where(x => keys.Contains(x.Key)).ToListAsync();
        return groups.Count == keys.Count ? groups.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase) : null;
    }

    private static void Apply(SnTest test, TestUpsertRequest request, IReadOnlyDictionary<string, SnTestQuestionGroup> groups)
    {
        test.Key = request.Key.Trim(); test.Title = request.Title; test.Description = request.Description; test.IsPublished = request.IsPublished; test.IsListed = request.IsListed; test.ShuffleQuestions = request.ShuffleQuestions; test.RandomQuestionCount = request.ShuffleQuestions ? request.RandomQuestionCount : null;
        test.PassingScore = request.PassingScore; test.MaxAttempts = request.MaxAttempts; test.AttemptPeriodDays = request.AttemptPeriodDays; test.TimeLimitSeconds = request.TimeLimitSeconds; test.GrantedPermissionGroupKey = string.IsNullOrWhiteSpace(request.GrantedPermissionGroupKey) ? null : request.GrantedPermissionGroupKey.Trim(); test.Config = request.Config;
        test.QuestionGroups = request.QuestionGroups.Select(x => new SnTestQuestionGroupAssignment { QuestionGroup = groups[x.QuestionGroupKey.Trim()], SortOrder = x.SortOrder }).ToList();
    }

    private static bool Validate(TestUpsertRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Title) || request.PassingScore is < 0 or > 100 || request.MaxAttempts is < 1 || request.AttemptPeriodDays < 1 || request.TimeLimitSeconds is < 1 || request.RandomQuestionCount is < 1 || request.QuestionGroups.Any(x => string.IsNullOrWhiteSpace(x.QuestionGroupKey)) || request.QuestionGroups.Select(x => x.QuestionGroupKey.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.QuestionGroups.Count) { error = "The test configuration is invalid."; return false; }
        error = string.Empty; return true;
    }
}

public class TestUpsertRequest { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public bool IsPublished { get; set; } public bool IsListed { get; set; } = true; public bool ShuffleQuestions { get; set; } public int? RandomQuestionCount { get; set; } public double PassingScore { get; set; } = 100; public int? MaxAttempts { get; set; } public int AttemptPeriodDays { get; set; } = 365; public int? TimeLimitSeconds { get; set; } public string? GrantedPermissionGroupKey { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); public List<TestQuestionGroupAssignmentUpsertRequest> QuestionGroups { get; set; } = []; }
public class TestQuestionGroupAssignmentUpsertRequest { public string QuestionGroupKey { get; set; } = null!; public int SortOrder { get; set; } }
public class ReviewTestAnswerRequest { public bool IsCorrect { get; set; } public double AwardedPoints { get; set; } public string? Note { get; set; } }
