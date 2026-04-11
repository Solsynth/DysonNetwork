using DysonNetwork.Sphere.ActivityPub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.ActivityPub;

[ApiController]
[Route("api/admin/fediverse/keys")]
[Authorize(Roles = "Admin")]
public class FediverseKeyAdminController(
    KeyMigrationService keyMigrationService,
    AppDatabase db,
    ILogger<FediverseKeyAdminController> logger
) : ControllerBase
{
    [HttpGet("audit")]
    public async Task<ActionResult<KeyAuditResult>> GetAuditResult()
    {
        var result = await keyMigrationService.AuditKeysAsync();
        return Ok(result);
    }

    [HttpPost("backfill")]
    public async Task<ActionResult<KeyMigrationResult>> BackfillKeys()
    {
        logger.LogInformation("Starting key backfill via admin API");

        var migrated = await keyMigrationService.MigratePublicKeyToNewTableAsync();
        var created = await keyMigrationService.BackfillKeysForExistingActorsAsync();

        var result = new KeyMigrationResult
        {
            MigratedFromActor = migrated,
            NewlyCreated = created,
            Total = migrated + created
        };

        logger.LogInformation("Key backfill complete: {Result}", result);
        return Ok(result);
    }

    [HttpPost("cleanup")]
    public async Task<ActionResult<int>> CleanupOrphanedKeys()
    {
        var count = await keyMigrationService.RemoveOrphanedKeysAsync();
        return Ok(new { removed = count });
    }

    [HttpGet("stats")]
    public async Task<ActionResult<KeyStatistics>> GetStatistics()
    {
        var stats = new KeyStatistics
        {
            TotalKeys = await db.FediverseKeys.CountAsync(),
            KeysWithPrivateKey = await db.FediverseKeys.CountAsync(k => !string.IsNullOrEmpty(k.PrivateKeyPem)),
            KeysWithoutPrivateKey = await db.FediverseKeys.CountAsync(k => string.IsNullOrEmpty(k.PrivateKeyPem)),
            LocalActors = await db.FediverseActors.CountAsync(a => a.PublisherId != null)
        };

        return Ok(stats);
    }

    [HttpGet("actor/{actorId:guid}")]
    public async Task<ActionResult<ActorKeyInfo>> GetActorKeyInfo(Guid actorId)
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == actorId);

        if (actor == null)
            return NotFound();

        var key = await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.ActorId == actorId);

        return Ok(new ActorKeyInfo
        {
            ActorId = actor.Id,
            ActorUri = actor.Uri,
            Username = actor.Username,
            Domain = actor.Instance?.Domain,
            HasKey = key != null,
            HasPrivateKey = key != null && !string.IsNullOrEmpty(key.PrivateKeyPem),
            KeyId = key?.KeyId,
            KeyCreatedAt = key?.CreatedAt.ToDateTimeOffset().DateTime,
            KeyRotatedAt = key?.RotatedAt?.ToDateTimeOffset().DateTime
        });
    }

    [HttpPost("actor/{actorId:guid}/regenerate")]
    public async Task<ActionResult> RegenerateKey(Guid actorId)
    {
        var actor = await db.FediverseActors.FindAsync(actorId);
        if (actor == null)
            return NotFound();

        if (!actor.PublisherId.HasValue)
            return BadRequest("Cannot regenerate key for actor without publisher");

        var success = await keyMigrationService.EnsureKeyExistsForActorAsync(actorId);
        if (!success)
            return BadRequest("Failed to regenerate key");

        return Ok(new { message = "Key regenerated successfully" });
    }
}

public class KeyMigrationResult
{
    public int MigratedFromActor { get; set; }
    public int NewlyCreated { get; set; }
    public int Total { get; set; }
}

public class KeyStatistics
{
    public int TotalKeys { get; set; }
    public int KeysWithPrivateKey { get; set; }
    public int KeysWithoutPrivateKey { get; set; }
    public int LocalActors { get; set; }
}

public class ActorKeyInfo
{
    public Guid ActorId { get; set; }
    public string ActorUri { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string? Domain { get; set; }
    public bool HasKey { get; set; }
    public bool HasPrivateKey { get; set; }
    public string? KeyId { get; set; }
    public DateTime? KeyCreatedAt { get; set; }
    public DateTime? KeyRotatedAt { get; set; }
}