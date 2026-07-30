using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DysonNetwork.Passport.Examination;

[Authorize]
[ApiController]
[Route("/api/admin/test-question-groups")]
[ApiFeature("admin.tests.question-groups", Revision = 2)]
public class TestQuestionGroupAdminController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<List<TestQuestionGroupResponse>>> List() =>
        Ok(await db.TestQuestionGroups.AsNoTracking().OrderBy(x => x.Key).Select(x => ToResponse(x)).ToListAsync());

    [HttpPost]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestQuestionGroupResponse>> Create([FromBody] TestQuestionGroupUpsertRequest request)
    {
        if (!Valid(request)) return BadRequest("The question group is invalid.");
        if (await db.TestQuestionGroups.AnyAsync(x => x.Key == request.Key.Trim())) return Conflict(ApiError.Conflict("A question group with this key already exists."));
        var group = new SnTestQuestionGroup();
        Apply(group, request);
        db.TestQuestionGroups.Add(group);
        await db.SaveChangesAsync();
        return Ok(await db.TestQuestionGroups.AsNoTracking().Where(x => x.Id == group.Id).Select(x => ToResponse(x)).FirstAsync());
    }

    [HttpPut("{key}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestQuestionGroupResponse>> Update(string key, [FromBody] TestQuestionGroupUpsertRequest request)
    {
        var group = await db.TestQuestionGroups.FirstOrDefaultAsync(x => x.Key == key);
        if (group is null) return NotFound();
        if (!Valid(request)) return BadRequest("The question group is invalid.");
        if (await db.TestQuestionGroups.AnyAsync(x => x.Id != group.Id && x.Key == request.Key.Trim())) return Conflict(ApiError.Conflict("A question group with this key already exists."));
        Apply(group, request);
        await db.SaveChangesAsync();
        return Ok(await db.TestQuestionGroups.AsNoTracking().Where(x => x.Id == group.Id).Select(x => ToResponse(x)).FirstAsync());
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

    private static bool Valid(TestQuestionGroupUpsertRequest request) => !string.IsNullOrWhiteSpace(request.Key) && !string.IsNullOrWhiteSpace(request.Title);
    private static void Apply(SnTestQuestionGroup group, TestQuestionGroupUpsertRequest request) { group.Key = request.Key.Trim(); group.Title = request.Title; group.Description = request.Description; group.Config = request.Config; }
    internal static Expression<Func<SnTestQuestionGroup, TestQuestionGroupResponse>> ToResponse() => x => new TestQuestionGroupResponse { Id = x.Id, Key = x.Key, Title = x.Title, Description = x.Description, Config = x.Config, QuestionCount = x.Questions.Count };
    internal static TestQuestionGroupResponse ToResponse(SnTestQuestionGroup group) => new() { Id = group.Id, Key = group.Key, Title = group.Title, Description = group.Description, Config = group.Config, QuestionCount = group.Questions.Count };
}

public class TestQuestionGroupUpsertRequest { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); }
public class TestQuestionGroupResponse { public Guid Id { get; set; } public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public Dictionary<string, object?> Config { get; set; } = new(); public int QuestionCount { get; set; } }
