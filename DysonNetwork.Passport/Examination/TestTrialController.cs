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
[Route("/api/tests/trials")]
[ApiFeature("tests.trials", Revision = 1)]
public class TestTrialController(AppDatabase db, TestService tests) : ControllerBase
{
    [HttpGet("{key}")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ParticipantTest>> Get(string key)
    {
        var trial = await Load(key);
        return trial is null ? NotFound() : Ok(TestController.ToParticipantTest(trial.Test, includeQuestions: false));
    }

    [HttpPost("{key}/attempts")]
    [AskPermission(PermissionKeys.TestsTake)]
    public async Task<ActionResult<ParticipantAttempt>> Start(string key)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount user) return Unauthorized();
        var trial = await Load(key);
        if (trial is null) return NotFound();
        try { return Ok(TestController.ToParticipantAttempt(await tests.StartAttempt(user.Id, trial.Test, isTrial: true, trialId: trial.Id, cancellationToken: HttpContext.RequestAborted))); }
        catch (InvalidOperationException ex) { return BadRequest(new ApiError { Code = "PASSPORT_TEST_TRIAL_UNAVAILABLE", Message = ex.Message, Status = 400 }); }
    }

    private Task<SnTestTrial?> Load(string key) => db.TestTrials.Include(x => x.Test).ThenInclude(x => x.QuestionGroups).ThenInclude(x => x.QuestionGroup).ThenInclude(x => x.Questions).ThenInclude(x => x.Choices)
        .FirstOrDefaultAsync(x => x.Key == key && x.IsPublished && !x.Test.IsArchived);
}
