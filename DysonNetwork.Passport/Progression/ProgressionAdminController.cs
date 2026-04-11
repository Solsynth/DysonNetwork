using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Progression;

[Authorize]
[ApiController]
[Route("/api/admin/progression")]
public class ProgressionAdminController(
    AppDatabase db,
    ProgressionSeedService seedService,
    RemoteRingService ring,
    DyPermissionService.DyPermissionServiceClient permissionService
) : ControllerBase
{
    [HttpGet("achievements")]
    public async Task<ActionResult<List<SnAchievementDefinition>>> ListAchievements()
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await db.AchievementDefinitions.OrderBy(m => m.SortOrder).ThenBy(m => m.Title).ToListAsync());
    }

    [HttpPost("achievements")]
    public async Task<ActionResult<SnAchievementDefinition>> CreateAchievement([FromBody] ProgressionDefinitionUpsertRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();
        var entity = new SnAchievementDefinition();
        ApplyAchievement(entity, request);
        db.AchievementDefinitions.Add(entity);
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("achievements/{identifier}")]
    public async Task<ActionResult<SnAchievementDefinition>> UpdateAchievement(string identifier, [FromBody] ProgressionDefinitionUpsertRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();
        var entity = await db.AchievementDefinitions.FirstOrDefaultAsync(m => m.Identifier == identifier);
        if (entity is null) return NotFound(ApiError.NotFound(identifier, traceId: HttpContext.TraceIdentifier));
        ApplyAchievement(entity, request);
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPost("achievements/{identifier}/enable")]
    public async Task<ActionResult<SnAchievementDefinition>> ToggleAchievement(string identifier, [FromQuery] bool enabled = true)
    {
        if (!await IsAdminAsync()) return Forbid();
        var entity = await db.AchievementDefinitions.FirstOrDefaultAsync(m => m.Identifier == identifier);
        if (entity is null) return NotFound(ApiError.NotFound(identifier, traceId: HttpContext.TraceIdentifier));
        entity.IsEnabled = enabled;
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpGet("quests")]
    public async Task<ActionResult<List<SnQuestDefinition>>> ListQuests()
    {
        if (!await IsAdminAsync()) return Forbid();
        return Ok(await db.QuestDefinitions.OrderBy(m => m.SortOrder).ThenBy(m => m.Title).ToListAsync());
    }

    [HttpPost("quests")]
    public async Task<ActionResult<SnQuestDefinition>> CreateQuest([FromBody] QuestDefinitionUpsertRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();
        var entity = new SnQuestDefinition();
        ApplyQuest(entity, request);
        db.QuestDefinitions.Add(entity);
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("quests/{identifier}")]
    public async Task<ActionResult<SnQuestDefinition>> UpdateQuest(string identifier, [FromBody] QuestDefinitionUpsertRequest request)
    {
        if (!await IsAdminAsync()) return Forbid();
        var entity = await db.QuestDefinitions.FirstOrDefaultAsync(m => m.Identifier == identifier);
        if (entity is null) return NotFound(ApiError.NotFound(identifier, traceId: HttpContext.TraceIdentifier));
        ApplyQuest(entity, request);
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPost("quests/{identifier}/enable")]
    public async Task<ActionResult<SnQuestDefinition>> ToggleQuest(string identifier, [FromQuery] bool enabled = true)
    {
        if (!await IsAdminAsync()) return Forbid();
        var entity = await db.QuestDefinitions.FirstOrDefaultAsync(m => m.Identifier == identifier);
        if (entity is null) return NotFound(ApiError.NotFound(identifier, traceId: HttpContext.TraceIdentifier));
        entity.IsEnabled = enabled;
        await db.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncSeeds()
    {
        if (!await IsAdminAsync()) return Forbid();
        await seedService.EnsureSeededAsync(HttpContext.RequestAborted);
        return Ok();
    }

    [HttpPost("test-ws-packet")]
    public async Task<IActionResult> TestWebSocketPacket([FromQuery] string? accountId, [FromQuery] string kind = "achievement", [FromQuery] string? identifier = null, [FromQuery] string? title = null)
    {
        if (!await IsAdminAsync()) return Forbid();
        var currentUser = HttpContext.Items["CurrentUser"] as SnAccount;
        var userId = accountId is not null ? Guid.Parse(accountId) : currentUser?.Id;
        if (userId is null) return BadRequest("No account specified and no current user");
        var packet = new ProgressionCompletionPacket
        {
            Kind = kind,
            Identifier = identifier ?? "test",
            Title = title ?? "Test Packet",
            PeriodKey = string.Empty,
            Reward = new SnProgressRewardDefinition()
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(packet);
        await ring.SendWebSocketPacketToUser(userId.Value.ToString(), "progression.completed", payload);
        return Ok();
    }

    private async Task<bool> IsAdminAsync()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return false;
        if (currentUser.IsSuperuser) return true;

        var response = await permissionService.HasPermissionAsync(new DyHasPermissionRequest
        {
            Actor = currentUser.Id.ToString(),
            Key = "progression.admin"
        });

        return response.HasPermission;
    }

    private static void ApplyAchievement(SnAchievementDefinition entity, ProgressionDefinitionUpsertRequest request)
    {
        entity.Identifier = request.Identifier;
        entity.Title = request.Title;
        entity.Summary = request.Summary;
        entity.Icon = request.Icon;
        entity.SeriesIdentifier = request.SeriesIdentifier;
        entity.SeriesTitle = request.SeriesTitle;
        entity.SeriesOrder = request.SeriesOrder;
        entity.SortOrder = request.SortOrder;
        entity.Hidden = request.Hidden;
        entity.IsEnabled = request.IsEnabled;
        entity.IsSeedManaged = request.IsSeedManaged;
        entity.IsProgressEnabled = request.IsProgressEnabled;
        entity.AvailableFrom = request.AvailableFrom;
        entity.AvailableUntil = request.AvailableUntil;
        entity.TargetCount = request.TargetCount;
        entity.Trigger = request.Trigger;
        entity.Reward = request.Reward;
    }

    private static void ApplyQuest(SnQuestDefinition entity, QuestDefinitionUpsertRequest request)
    {
        entity.Identifier = request.Identifier;
        entity.Title = request.Title;
        entity.Summary = request.Summary;
        entity.Icon = request.Icon;
        entity.SeriesIdentifier = request.SeriesIdentifier;
        entity.SeriesTitle = request.SeriesTitle;
        entity.SeriesOrder = request.SeriesOrder;
        entity.SortOrder = request.SortOrder;
        entity.Hidden = request.Hidden;
        entity.IsEnabled = request.IsEnabled;
        entity.IsSeedManaged = request.IsSeedManaged;
        entity.IsProgressEnabled = request.IsProgressEnabled;
        entity.AvailableFrom = request.AvailableFrom;
        entity.AvailableUntil = request.AvailableUntil;
        entity.TargetCount = request.TargetCount;
        entity.Trigger = request.Trigger;
        entity.Reward = request.Reward;
        entity.Schedule = request.Schedule;
    }
}
