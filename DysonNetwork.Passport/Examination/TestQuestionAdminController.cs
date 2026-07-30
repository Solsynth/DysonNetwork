using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Text;

namespace DysonNetwork.Passport.Examination;

[Authorize]
[ApiController]
[Route("/api/admin/test-questions")]
[ApiFeature("admin.tests.questions", Revision = 1)]
public class TestQuestionAdminController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestQuestionPage>> List([FromQuery] string groupKey, [FromQuery] int take = 20, [FromQuery] int offset = 0)
    {
        if (string.IsNullOrWhiteSpace(groupKey)) return BadRequest("A question group key is required.");
        take = Math.Clamp(take, 1, 100); offset = Math.Max(offset, 0);
        var query = db.TestQuestions.AsNoTracking().Include(x => x.Choices).Where(x => x.QuestionGroup.Key == groupKey);
        return Ok(new TestQuestionPage { TotalCount = await query.CountAsync(), Items = await query.OrderBy(x => x.SortOrder).Skip(offset).Take(take).Select(ToResponse()).ToListAsync() });
    }

    [HttpPost]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestQuestionResponse>> Create([FromBody] TestQuestionUpsertRequest request)
    {
        if (!Valid(request)) return BadRequest("The question is invalid.");
        var group = await db.TestQuestionGroups.FirstOrDefaultAsync(x => x.Key == request.QuestionGroupKey);
        if (group is null) return NotFound();
        var question = new SnTestQuestion { QuestionGroupId = group.Id, SortOrder = request.SortOrder ?? await db.TestQuestions.Where(x => x.QuestionGroupId == group.Id).CountAsync() };
        Apply(question, request);
        db.TestQuestions.Add(question);
        await db.SaveChangesAsync();
        return Ok(await Questions().FirstAsync(x => x.Id == question.Id));
    }

    [HttpPost("import")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestQuestionImportResult>> Import([FromBody] TestQuestionImportRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.QuestionGroupKey) || request.Questions.Count == 0) return BadRequest("A question group and at least one question are required.");
        foreach (var question in request.Questions) question.QuestionGroupKey = request.QuestionGroupKey;
        if (request.Questions.Any(question => !Valid(question))) return BadRequest("One or more imported questions are invalid.");
        var group = await db.TestQuestionGroups.FirstOrDefaultAsync(x => x.Key == request.QuestionGroupKey);
        if (group is null) return NotFound();
        var sortOrder = await db.TestQuestions.Where(x => x.QuestionGroupId == group.Id).CountAsync();
        foreach (var requestQuestion in request.Questions)
        {
            var question = new SnTestQuestion { QuestionGroupId = group.Id, SortOrder = requestQuestion.SortOrder ?? sortOrder++ };
            Apply(question, requestQuestion);
            db.TestQuestions.Add(question);
        }
        await db.SaveChangesAsync();
        return Ok(new TestQuestionImportResult { ImportedCount = request.Questions.Count });
    }

    [HttpGet("export")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<FileContentResult> Export([FromQuery] string? groupKey)
    {
        var questions = await db.TestQuestions.AsNoTracking().Include(x => x.QuestionGroup).Include(x => x.Choices)
            .Where(x => string.IsNullOrWhiteSpace(groupKey) || x.QuestionGroup.Key == groupKey)
            .OrderBy(x => x.QuestionGroup.Key).ThenBy(x => x.SortOrder).ToListAsync();
        var csv = new StringBuilder("question_group_key,content,category,type,grading_mode,difficulty,points,choices,correct_choices\n");
        foreach (var question in questions)
        {
            var choices = question.Choices.OrderBy(x => x.SortOrder).ToList();
            csv.AppendLine(string.Join(',', [
                Csv(question.QuestionGroup.Key), Csv(question.Content), Csv(question.Category), Csv(question.Type switch { TestQuestionType.SingleChoice => "single_choice", TestQuestionType.MultipleChoice => "multiple_choice", _ => "free_text" }),
                Csv(question.GradingMode == TestQuestionGradingMode.Auto ? "auto" : "manual"), question.Difficulty, question.Points,
                Csv(string.Join('|', choices.Select(x => x.Content))), Csv(string.Join('|', choices.Select((x, index) => (x, index)).Where(x => x.x.IsCorrect).Select(x => x.index)))
            ]));
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"test-question-bank{(string.IsNullOrWhiteSpace(groupKey) ? string.Empty : $"-{groupKey}")}.csv");
    }

    [HttpDelete("prune")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestQuestionPruneResult>> Prune([FromQuery] string? groupKey, [FromQuery] bool confirm = false)
    {
        if (!confirm) return BadRequest("Set confirm=true to prune question-bank questions.");
        var query = db.TestQuestions.Where(x => string.IsNullOrWhiteSpace(groupKey) || x.QuestionGroup.Key == groupKey);
        var removedCount = await query.CountAsync();
        await query.ExecuteDeleteAsync();
        return Ok(new TestQuestionPruneResult { RemovedCount = removedCount });
    }

    [HttpPut("{id:guid}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestQuestionResponse>> Update(Guid id, [FromBody] TestQuestionUpsertRequest request)
    {
        var question = await db.TestQuestions.Include(x => x.Choices).FirstOrDefaultAsync(x => x.Id == id);
        if (question is null) return NotFound();
        if (!Valid(request)) return BadRequest("The question is invalid.");
        var group = await db.TestQuestionGroups.FirstOrDefaultAsync(x => x.Key == request.QuestionGroupKey);
        if (group is null) return NotFound();
        question.QuestionGroupId = group.Id;
        if (request.SortOrder.HasValue) question.SortOrder = request.SortOrder.Value;
        Apply(question, request);
        await db.SaveChangesAsync();
        return Ok(await Questions().FirstAsync(x => x.Id == question.Id));
    }

    [HttpDelete("{id:guid}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var question = await db.TestQuestions.FirstOrDefaultAsync(x => x.Id == id);
        if (question is null) return NotFound();
        db.TestQuestions.Remove(question);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<TestQuestionResponse> Questions() => db.TestQuestions.AsNoTracking().Include(x => x.Choices).Select(ToResponse());
    private static string Csv(string? value) => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    private static Expression<Func<SnTestQuestion, TestQuestionResponse>> ToResponse() => x => new TestQuestionResponse { Id = x.Id, SortOrder = x.SortOrder, Content = x.Content, Category = x.Category, Type = x.Type, GradingMode = x.GradingMode, Difficulty = x.Difficulty, Points = x.Points, Config = x.Config, Choices = x.Choices.OrderBy(c => c.SortOrder).Select(c => new TestChoiceResponse { Id = c.Id, SortOrder = c.SortOrder, Content = c.Content, IsCorrect = c.IsCorrect, Config = c.Config }).ToList() };
    private static bool Valid(TestQuestionUpsertRequest request) => !string.IsNullOrWhiteSpace(request.QuestionGroupKey) && !string.IsNullOrWhiteSpace(request.Content) && request.Points >= 0 && !(request.GradingMode == TestQuestionGradingMode.Auto && (request.Type == TestQuestionType.FreeText || !request.Choices.Any(x => x.IsCorrect)));
    private void Apply(SnTestQuestion question, TestQuestionUpsertRequest request)
    {
        question.Content = request.Content; question.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(); question.Type = request.Type; question.GradingMode = request.Type == TestQuestionType.FreeText ? TestQuestionGradingMode.Manual : request.GradingMode; question.Difficulty = request.Difficulty; question.Points = request.Points; question.Config = request.Config;
        var existing = question.Choices.ToDictionary(x => x.Id);
        question.Choices = request.Choices.Select((choice, index) =>
        {
            var item = choice.Id.HasValue && existing.Remove(choice.Id.Value) ? question.Choices.First(x => x.Id == choice.Id.Value) : new SnTestChoice();
            item.SortOrder = choice.SortOrder; item.Content = choice.Content; item.IsCorrect = choice.IsCorrect; item.Config = choice.Config;
            return item;
        }).ToList();
        db.TestChoices.RemoveRange(existing.Values);
    }
}

public class TestQuestionPage { public int TotalCount { get; set; } public List<TestQuestionResponse> Items { get; set; } = []; }
public class TestQuestionImportResult { public int ImportedCount { get; set; } }
public class TestQuestionPruneResult { public int RemovedCount { get; set; } }
public class TestQuestionResponse { public Guid Id { get; set; } public int SortOrder { get; set; } public string Content { get; set; } = null!; public string? Category { get; set; } public TestQuestionType Type { get; set; } public TestQuestionGradingMode GradingMode { get; set; } public int Difficulty { get; set; } public double Points { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); public List<TestChoiceResponse> Choices { get; set; } = []; }
public class TestChoiceResponse { public Guid Id { get; set; } public int SortOrder { get; set; } public string Content { get; set; } = null!; public bool IsCorrect { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); }
public class TestQuestionUpsertRequest { public string? QuestionGroupKey { get; set; } public int? SortOrder { get; set; } public string Content { get; set; } = null!; public string? Category { get; set; } public TestQuestionType Type { get; set; } public TestQuestionGradingMode GradingMode { get; set; } public int Difficulty { get; set; } public double Points { get; set; } = 1; public Dictionary<string, object?> Config { get; set; } = new(); public List<TestChoiceUpsertRequest> Choices { get; set; } = []; }
public class TestChoiceUpsertRequest { public Guid? Id { get; set; } public int SortOrder { get; set; } public string Content { get; set; } = null!; public bool IsCorrect { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); }
public class TestQuestionImportRequest { public string QuestionGroupKey { get; set; } = null!; public List<TestQuestionUpsertRequest> Questions { get; set; } = []; }
