using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Permission;

public class PermissionSeedService(
    AppDatabase db,
    ILogger<PermissionSeedService> logger)
{
    public const string DefaultGroupKey = "default";
    public const string AllUsersGroupKey = "all-users";

    /// <summary>
    /// Synchronizes all keys defined in <see cref="PermissionKeys"/> into the default
    /// permission group. Missing keys are inserted; existing keys are skipped.
    /// Run at service startup so newly added PermissionKeys are always picked up.
    /// </summary>
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var defaultKeys = typeof(PermissionKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Except([PermissionKeys.TestsManage, PermissionKeys.TestsReview])
            .ToHashSet();
        var defaultGroup = await EnsureGroupAsync(DefaultGroupKey, defaultKeys, cancellationToken);
        var allUsersGroup = await EnsureGroupAsync(AllUsersGroupKey, [PermissionKeys.TestsTake], cancellationToken);

        var accountActors = await db.Accounts.Select(x => x.Id.ToString()).ToListAsync(cancellationToken);
        var currentActors = await db.PermissionGroupMembers.Where(x => x.GroupId == allUsersGroup.Id)
            .Select(x => x.Actor).ToListAsync(cancellationToken);
        var missingActors = accountActors.Except(currentActors).ToList();
        foreach (var actor in missingActors)
        {
            db.PermissionGroupMembers.Add(new SnPermissionGroupMember { GroupId = allUsersGroup.Id, Actor = actor });
        }
        if (missingActors.Count > 0) await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Permission groups synchronized: {DefaultGroup} and {AllUsersGroup}.", defaultGroup.Key, allUsersGroup.Key);
    }

    private async Task<SnPermissionGroup> EnsureGroupAsync(string key, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var group = await db.PermissionGroups.Include(g => g.Nodes).FirstOrDefaultAsync(g => g.Key == key, cancellationToken);
        if (group is null)
        {
            group = new SnPermissionGroup { Key = key };
            db.PermissionGroups.Add(group);
            await db.SaveChangesAsync(cancellationToken);
        }
        var existing = group.Nodes.Select(x => x.Key).ToHashSet();
        foreach (var permissionKey in keys.Except(existing))
        {
            var node = new SnPermissionNode { Actor = $"group:{key}", Type = PermissionNodeActorType.Group, Key = permissionKey, Value = JsonDocument.Parse(JsonSerializer.Serialize(true)), GroupId = group.Id, Group = group };
            db.PermissionNodes.Add(node);
            group.Nodes.Add(node);
        }
        await db.SaveChangesAsync(cancellationToken);
        return group;
    }
}
