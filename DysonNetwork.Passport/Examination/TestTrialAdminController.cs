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
[Route("/api/admin/test-trials")]
[ApiFeature("admin.tests.trials", Revision = 1)]
public class TestTrialAdminController(AppDatabase db) : ControllerBase
{
    [HttpGet]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<List<TestTrialResponse>>> List() =>
        Ok((await db.TestTrials.AsNoTracking().Include(x => x.Test).OrderBy(x => x.Key).ToListAsync()).Select(ToResponse).ToList());

    [HttpPost]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestTrialResponse>> Create([FromBody] TestTrialUpsertRequest request)
    {
        if (!Valid(request)) return BadRequest("The trial configuration is invalid.");
        if (await db.TestTrials.AnyAsync(x => x.Key == request.Key.Trim())) return Conflict(ApiError.Conflict("A trial with this key already exists."));
        var test = await db.Tests.FirstOrDefaultAsync(x => x.Key == request.TestKey && !x.IsArchived);
        if (test is null) return BadRequest("The selected test is unavailable.");
        var trial = new SnTestTrial { TestId = test.Id };
        Apply(trial, request);
        db.TestTrials.Add(trial);
        await db.SaveChangesAsync();
        return Ok(ToResponse(await db.TestTrials.AsNoTracking().Include(x => x.Test).FirstAsync(x => x.Id == trial.Id)));
    }

    [HttpPut("{key}")]
    [AskPermission(PermissionKeys.TestsManage)]
    public async Task<ActionResult<TestTrialResponse>> Update(string key, [FromBody] TestTrialUpsertRequest request)
    {
        var trial = await db.TestTrials.FirstOrDefaultAsync(x => x.Key == key);
        if (trial is null) return NotFound();
        if (!Valid(request)) return BadRequest("The trial configuration is invalid.");
        if (await db.TestTrials.AnyAsync(x => x.Id != trial.Id && x.Key == request.Key.Trim())) return Conflict(ApiError.Conflict("A trial with this key already exists."));
        var test = await db.Tests.FirstOrDefaultAsync(x => x.Key == request.TestKey && !x.IsArchived);
        if (test is null) return BadRequest("The selected test is unavailable.");
        trial.TestId = test.Id;
        Apply(trial, request);
        await db.SaveChangesAsync();
        return Ok(ToResponse(await db.TestTrials.AsNoTracking().Include(x => x.Test).FirstAsync(x => x.Id == trial.Id)));
    }

    private static bool Valid(TestTrialUpsertRequest request) => !string.IsNullOrWhiteSpace(request.Key) && !string.IsNullOrWhiteSpace(request.Title) && !string.IsNullOrWhiteSpace(request.TestKey);
    private static void Apply(SnTestTrial trial, TestTrialUpsertRequest request) { trial.Key = request.Key.Trim(); trial.Title = request.Title; trial.Description = request.Description; trial.IsPublished = request.IsPublished; }
    private static TestTrialResponse ToResponse(SnTestTrial trial) => new() { Key = trial.Key, Title = trial.Title, Description = trial.Description, IsPublished = trial.IsPublished, TestKey = trial.Test.Key, TestTitle = trial.Test.Title };
}

public class TestTrialUpsertRequest { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public bool IsPublished { get; set; } = true; public string TestKey { get; set; } = null!; }
public class TestTrialResponse { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public string? Description { get; set; } public bool IsPublished { get; set; } public string TestKey { get; set; } = null!; public string TestTitle { get; set; } = null!; }
