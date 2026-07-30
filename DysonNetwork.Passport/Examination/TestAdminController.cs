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
        Apply(test, request);
        db.Tests.Add(test);
        db.TestQuestionGroupAssignments.AddRange(CreateAssignments(test.Id, request, groups));
        await db.SaveChangesAsync();
        return Ok(await TestsQuery().FirstAsync(x => x.Id == test.Id));
    }

    [HttpPut("{key}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTest>> Update(string key, [FromBody] TestUpsertRequest request)
    {
        var test = await db.Tests.FirstOrDefaultAsync(x => x.Key == key);
        if (test is null) return NotFound();
        if (!Validate(request, out var error)) return BadRequest(error);
        var groups = await ResolveGroups(request.QuestionGroups);
        if (groups is null) return BadRequest("One or more question groups do not exist.");
        await db.TestQuestionGroupAssignments.Where(x => x.TestId == test.Id).ExecuteDeleteAsync();
        Apply(test, request);
        db.TestQuestionGroupAssignments.AddRange(CreateAssignments(test.Id, request, groups));
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

    [HttpGet("{key}/trial")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<ParticipantTest>> Trial(string key)
    {
        var test = await TestsQuery().FirstOrDefaultAsync(x => x.Key == key && !x.IsArchived);
        return test is null ? NotFound() : Ok(TestController.ToParticipantTest(test));
    }

    [HttpPost("{key}/trial/grade")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestTrialResult>> GradeTrial(string key, [FromBody] SubmitTestAttemptRequest request)
    {
        var test = await TestsQuery().FirstOrDefaultAsync(x => x.Key == key && !x.IsArchived);
        if (test is null) return NotFound();
        var inputs = request.Answers.GroupBy(x => x.QuestionId).ToDictionary(x => x.Key, x => x.Last());
        var questions = test.QuestionGroups.SelectMany(x => x.QuestionGroup.Questions).Where(question => inputs.ContainsKey(question.Id)).ToList();
        var answers = questions.Select(question =>
        {
            if (question.GradingMode == TestQuestionGradingMode.Manual) return new TestTrialAnswerResult { QuestionId = question.Id };
            inputs.TryGetValue(question.Id, out var input);
            var wasAnswered = input?.ChoiceIds?.Count > 0 || !string.IsNullOrWhiteSpace(input?.Text);
            if (!wasAnswered) return new TestTrialAnswerResult { QuestionId = question.Id, AwardedPoints = 0 };
            var selected = (input?.ChoiceIds ?? []).Order().ToArray();
            var correct = question.Choices.Where(choice => choice.IsCorrect).Select(choice => choice.Id).Order().ToArray();
            var isCorrect = selected.SequenceEqual(correct);
            return new TestTrialAnswerResult { QuestionId = question.Id, IsCorrect = isCorrect, AwardedPoints = isCorrect ? question.Points : -question.Points };
        }).ToList();
        var possible = questions.Where(x => x.GradingMode == TestQuestionGradingMode.Auto).Sum(x => x.Points);
        var score = possible == 0 ? (double?)null : answers.Sum(x => x.AwardedPoints ?? 0) / possible * 100;
        return Ok(new TestTrialResult { Score = score, Passed = score.HasValue && score >= test.PassingScore, Answers = answers });
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

    private static void Apply(SnTest test, TestUpsertRequest request)
    {
        test.Key = request.Key.Trim(); test.Title = request.Title; test.Description = request.Description; test.IsPublished = request.IsPublished; test.IsListed = request.IsListed; test.ShuffleQuestions = request.ShuffleQuestions; test.RandomQuestionCount = request.ShuffleQuestions ? request.RandomQuestionCount : null; test.SimpleQuestionPercentage = request.SimpleQuestionPercentage;
        test.PassingScore = request.PassingScore; test.MaxAttempts = request.MaxAttempts; test.AttemptPeriodDays = request.AttemptPeriodDays; test.TimeLimitSeconds = request.TimeLimitSeconds; test.GrantedPermissionGroupKey = string.IsNullOrWhiteSpace(request.GrantedPermissionGroupKey) ? null : request.GrantedPermissionGroupKey.Trim(); test.Config = request.Config;
    }

    private static IEnumerable<SnTestQuestionGroupAssignment> CreateAssignments(Guid testId, TestUpsertRequest request, IReadOnlyDictionary<string, SnTestQuestionGroup> groups) => request.QuestionGroups.Select(x => new SnTestQuestionGroupAssignment
    {
        TestId = testId,
        QuestionGroupId = groups[x.QuestionGroupKey.Trim()].Id,
        SortOrder = x.SortOrder
    });

    private static bool Validate(TestUpsertRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Title) || request.PassingScore is < 0 or > 100 || request.MaxAttempts is < 1 || request.AttemptPeriodDays < 1 || request.TimeLimitSeconds is < 1 || request.RandomQuestionCount is < 1 || request.SimpleQuestionPercentage is < 0 or > 100 || request.QuestionGroups.Any(x => string.IsNullOrWhiteSpace(x.QuestionGroupKey)) || request.QuestionGroups.Select(x => x.QuestionGroupKey.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != request.QuestionGroups.Count) { error = "The test configuration is invalid."; return false; }
        error = string.Empty; return true;
    }
}

public class TestUpsertRequest { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public bool IsPublished { get; set; } public bool IsListed { get; set; } = true; public bool ShuffleQuestions { get; set; } public int? RandomQuestionCount { get; set; } public int SimpleQuestionPercentage { get; set; } = 60; public double PassingScore { get; set; } = 100; public int? MaxAttempts { get; set; } public int AttemptPeriodDays { get; set; } = 365; public int? TimeLimitSeconds { get; set; } public string? GrantedPermissionGroupKey { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); public List<TestQuestionGroupAssignmentUpsertRequest> QuestionGroups { get; set; } = []; }
public class TestQuestionGroupAssignmentUpsertRequest { public string QuestionGroupKey { get; set; } = null!; public int SortOrder { get; set; } }
public class ReviewTestAnswerRequest { public bool IsCorrect { get; set; } public double AwardedPoints { get; set; } public string? Note { get; set; } }
public class TestTrialResult { public double? Score { get; set; } public bool Passed { get; set; } public List<TestTrialAnswerResult> Answers { get; set; } = []; }
public class TestTrialAnswerResult { public Guid QuestionId { get; set; } public bool? IsCorrect { get; set; } public double? AwardedPoints { get; set; } }
