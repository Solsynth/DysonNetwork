using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Examination;

[Authorize]
[ApiController]
[Route("/api/admin/test-question-groups")]
[ApiFeature("admin.tests.question-groups", Revision = 1)]
public class TestQuestionGroupAdminController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<List<SnTestQuestionGroup>>> List() => Ok(await GroupsQuery().OrderBy(x => x.Key).ToListAsync());

    [HttpPost]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTestQuestionGroup>> Create([FromBody] TestQuestionGroupUpsertRequest request)
    {
        if (!Validate(request, out var error)) return BadRequest(error);
        if (await db.TestQuestionGroups.AnyAsync(x => x.Key == request.Key.Trim())) return Conflict(ApiError.Conflict("A question group with this key already exists."));
        var group = new SnTestQuestionGroup();
        Apply(group, request);
        db.TestQuestionGroups.Add(group);
        await db.SaveChangesAsync();
        return Ok(await GroupsQuery().FirstAsync(x => x.Id == group.Id));
    }

    [HttpPut("{key}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<SnTestQuestionGroup>> Update(string key, [FromBody] TestQuestionGroupUpsertRequest request)
    {
        var group = await GroupsQuery().FirstOrDefaultAsync(x => x.Key == key);
        if (group is null) return NotFound();
        if (!Validate(request, out var error)) return BadRequest(error);
        if (await db.TestQuestionGroups.AnyAsync(x => x.Id != group.Id && x.Key == request.Key.Trim())) return Conflict(ApiError.Conflict("A question group with this key already exists."));
        db.TestQuestions.RemoveRange(group.Questions);
        Apply(group, request);
        await db.SaveChangesAsync();
        return Ok(await GroupsQuery().FirstAsync(x => x.Id == group.Id));
    }

    [HttpDelete("{key}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<IActionResult> Delete(string key)
    {
        var group = await db.TestQuestionGroups.Include(x => x.TestAssignments).FirstOrDefaultAsync(x => x.Key == key);
        if (group is null) return NotFound();
        if (group.TestAssignments.Count > 0) return Conflict(ApiError.Conflict("Remove this group from its tests before deleting it."));
        db.TestQuestionGroups.Remove(group);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private IQueryable<SnTestQuestionGroup> GroupsQuery() => db.TestQuestionGroups.Include(x => x.Questions).ThenInclude(x => x.Choices);

    private static void Apply(SnTestQuestionGroup group, TestQuestionGroupUpsertRequest request)
    {
        group.Key = request.Key.Trim(); group.Title = request.Title; group.Description = request.Description; group.Config = request.Config;
        group.Questions = request.Questions.Select(question => new SnTestQuestion
        {
            SortOrder = question.SortOrder, Content = question.Content, Type = question.Type, GradingMode = question.GradingMode, Difficulty = question.Difficulty, Points = question.Points, Config = question.Config,
            Choices = question.Choices.Select(choice => new SnTestChoice { SortOrder = choice.SortOrder, Content = choice.Content, IsCorrect = choice.IsCorrect, Config = choice.Config }).ToList()
        }).ToList();
    }

    private static bool Validate(TestQuestionGroupUpsertRequest request, out string error)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || string.IsNullOrWhiteSpace(request.Title) || request.Questions.Any(question => string.IsNullOrWhiteSpace(question.Content) || question.Points < 0 || (question.GradingMode == TestQuestionGradingMode.Auto && (question.Type == TestQuestionType.FreeText || !question.Choices.Any(choice => choice.IsCorrect))))) { error = "The question group is invalid."; return false; }
        error = string.Empty; return true;
    }
}

public class TestQuestionGroupUpsertRequest { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); public List<TestQuestionUpsertRequest> Questions { get; set; } = []; }
public class TestQuestionUpsertRequest { public int SortOrder { get; set; } public string Content { get; set; } = null!; public TestQuestionType Type { get; set; } public TestQuestionGradingMode GradingMode { get; set; } public int Difficulty { get; set; } public double Points { get; set; } = 1; public Dictionary<string, object?> Config { get; set; } = new(); public List<TestChoiceUpsertRequest> Choices { get; set; } = []; }
public class TestChoiceUpsertRequest { public int SortOrder { get; set; } public string Content { get; set; } = null!; public bool IsCorrect { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); }
