using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Permission;

public class PermissionSeedService(
    AppDatabase db,
    ILogger<PermissionSeedService> logger)
{
    private const string DefaultGroupKey = "default";

    /// <summary>
    /// Synchronizes all keys defined in <see cref="PermissionKeys"/> into the default
    /// permission group. Missing keys are inserted; existing keys are skipped.
    /// Run at service startup so newly added PermissionKeys are always picked up.
    /// </summary>
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var group = await db.PermissionGroups
            .Include(g => g.Nodes)
            .FirstOrDefaultAsync(g => g.Key == DefaultGroupKey, cancellationToken);

        if (group is null)
        {
            logger.LogWarning("Default permission group (key={GroupKey}) not found. Skipping permission seeding.", DefaultGroupKey);
            return;
        }

        var allKeys = typeof(PermissionKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();

        var existingKeys = group.Nodes
            .Where(n => n.Type == PermissionNodeActorType.Group)
            .Select(n => n.Key)
            .ToHashSet();

        var missing = allKeys.Except(existingKeys).ToList();
        if (missing.Count == 0)
        {
            logger.LogDebug("Default permission group is up to date ({Count} keys).", allKeys.Count);
            return;
        }

        foreach (var key in missing)
        {
            var node = new SnPermissionNode
            {
                Actor = $"group:{DefaultGroupKey}",
                Type = PermissionNodeActorType.Group,
                Key = key,
                Value = JsonDocument.Parse(JsonSerializer.Serialize(true)),
                GroupId = group.Id,
                Group = group
            };
            db.PermissionNodes.Add(node);
            group.Nodes.Add(node);
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Synced {MissingCount} new permission key(s) into default group ({TotalCount} total).",
            missing.Count, allKeys.Count);
    }
}
