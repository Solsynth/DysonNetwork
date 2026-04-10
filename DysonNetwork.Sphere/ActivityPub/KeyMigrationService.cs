using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class KeyMigrationService(
    AppDatabase db,
    IKeyService keyService,
    ILogger<KeyMigrationService> logger
)
{
    public static async Task InitializeOnStartupAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        var keyService = scope.ServiceProvider.GetRequiredService<IKeyService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<KeyMigrationService>>();
        
        var migrationService = new KeyMigrationService(db, keyService, logger);
        await migrationService.InitializeKeysOnStartupAsync();
    }

    public async Task InitializeKeysOnStartupAsync()
    {
        var hasAnyKeys = await db.FediverseKeys.AnyAsync();
        
        if (hasAnyKeys)
        {
            logger.LogDebug("Fediverse keys table already has data, skipping initialization");
            return;
        }

        logger.LogInformation("No fediverse keys found, running key initialization...");

        var migrated = await MigratePublicKeyToNewTableAsync();
        var created = await BackfillKeysForExistingActorsAsync();

        logger.LogInformation("Key initialization complete. Migrated: {Migrated}, Created: {Created}", migrated, created);
    }

    public async Task<int> BackfillKeysForExistingActorsAsync()
    {
        logger.LogInformation("Starting key backfill for existing actors");

        var actorsWithoutKeys = await db.FediverseActors
            .Where(a => a.PublisherId != null)
            .Where(a => !db.FediverseKeys.Any(k => k.ActorId == a.Id))
            .ToListAsync();

        logger.LogInformation("Found {Count} actors without keys", actorsWithoutKeys.Count);

        var created = 0;
        foreach (var actor in actorsWithoutKeys)
        {
            try
            {
                var key = await keyService.GetOrCreateKeyForActorAsync(actor);
                if (key != null)
                {
                    created++;
                    logger.LogDebug("Created key for actor: {ActorUri}", actor.Uri);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create key for actor: {ActorUri}", actor.Uri);
            }
        }

        logger.LogInformation("Key backfill complete. Created {Count} keys", created);
        return created;
    }

    public async Task<int> MigratePublicKeyToNewTableAsync()
    {
        logger.LogInformation("Starting public key migration to new table");

        var actorsWithPublicKey = await db.FediverseActors
            .Where(a => !string.IsNullOrEmpty(a.PublicKey))
            .Where(a => !db.FediverseKeys.Any(k => k.ActorId == a.Id))
            .ToListAsync();

        logger.LogInformation("Found {Count} actors with public key to migrate", actorsWithPublicKey.Count);

        var migrated = 0;
        foreach (var actor in actorsWithPublicKey)
        {
            try
            {
                var keyId = $"{actor.Uri}#main-key";

                var existingKey = await db.FediverseKeys
                    .FirstOrDefaultAsync(k => k.KeyId == keyId);

                if (existingKey == null)
                {
                    var key = new SnFediverseKey
                    {
                        KeyId = keyId,
                        KeyPem = actor.PublicKey!,
                        ActorId = actor.Id,
                        PublisherId = actor.PublisherId,
                        CreatedAt = SystemClock.Instance.GetCurrentInstant()
                    };

                    db.FediverseKeys.Add(key);
                    migrated++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to migrate key for actor: {ActorUri}", actor.Uri);
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("Key migration complete. Migrated {Count} keys", migrated);
        return migrated;
    }

    public async Task<bool> EnsureKeyExistsForActorAsync(Guid actorId)
    {
        var actor = await db.FediverseActors.FindAsync(actorId);
        if (actor == null)
            return false;

        var existingKey = await db.FediverseKeys
            .FirstOrDefaultAsync(k => k.ActorId == actorId);

        if (existingKey != null)
            return true;

        if (!actor.PublisherId.HasValue)
        {
            logger.LogWarning("Actor {ActorId} has no publisher, cannot create key", actorId);
            return false;
        }

        var key = await keyService.GetOrCreateKeyForActorAsync(actor);
        return key != null;
    }

    public async Task<KeyAuditResult> AuditKeysAsync()
    {
        var result = new KeyAuditResult();

        result.TotalActors = await db.FediverseActors.CountAsync(a => a.PublisherId != null);
        result.TotalKeys = await db.FediverseKeys.CountAsync();

        result.ActorsWithKeys = await db.FediverseActors
            .CountAsync(a => a.PublisherId != null && 
                            db.FediverseKeys.Any(k => k.ActorId == a.Id));

        result.ActorsWithoutKeys = result.TotalActors - result.ActorsWithKeys;

        result.KeysWithPrivateKey = await db.FediverseKeys
            .CountAsync(k => !string.IsNullOrEmpty(k.PrivateKeyPem));

        result.KeysWithoutPrivateKey = result.TotalKeys - result.KeysWithPrivateKey;

        var orphanedKeys = await db.FediverseKeys
            .Where(k => k.ActorId != null)
            .Where(k => !db.FediverseActors.Any(a => a.Id == k.ActorId))
            .ToListAsync();

        result.OrphanedKeys = orphanedKeys.Count;

        return result;
    }

    public async Task<int> RemoveOrphanedKeysAsync()
    {
        var orphanedKeys = await db.FediverseKeys
            .Where(k => k.ActorId != null)
            .Where(k => !db.FediverseActors.Any(a => a.Id == k.ActorId))
            .ToListAsync();

        if (orphanedKeys.Count > 0)
        {
            db.FediverseKeys.RemoveRange(orphanedKeys);
            await db.SaveChangesAsync();
            logger.LogInformation("Removed {Count} orphaned keys", orphanedKeys.Count);
        }

        return orphanedKeys.Count;
    }
}

public class KeyAuditResult
{
    public int TotalActors { get; set; }
    public int TotalKeys { get; set; }
    public int ActorsWithKeys { get; set; }
    public int ActorsWithoutKeys { get; set; }
    public int KeysWithPrivateKey { get; set; }
    public int KeysWithoutPrivateKey { get; set; }
    public int OrphanedKeys { get; set; }
}