using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.Permission;

[ApiController]
[Route("/api/admin/permissions")]
[Authorize]
public class PermissionAdminController(
    AppDatabase db,
    PermissionService permissionService
) : ControllerBase
{
    public class PermissionGroupSummary
    {
        public Guid Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public int NodeCount { get; set; }
        public int MemberCount { get; set; }
        public Instant CreatedAt { get; set; }
        public Instant UpdatedAt { get; set; }
    }

    public class PermissionGroupDetailResponse
    {
        public SnPermissionGroup Group { get; set; } = null!;
        public List<SnPermissionNode> Nodes { get; set; } = [];
        public List<SnPermissionGroupMember> Members { get; set; } = [];
    }

    public class AdminActorPermissionsResponse
    {
        public string Actor { get; set; } = string.Empty;
        public List<SnPermissionNode> DirectPermissions { get; set; } = [];
        public List<SnPermissionNode> EffectivePermissions { get; set; } = [];
        public List<SnPermissionGroupMember> Groups { get; set; } = [];
    }

    public class CreatePermissionGroupRequest
    {
        [Required, MaxLength(1024)] public string Key { get; set; } = string.Empty;
    }

    public class UpdatePermissionGroupRequest
    {
        [Required, MaxLength(1024)] public string Key { get; set; } = string.Empty;
    }

    public class UpsertGroupPermissionRequest
    {
        public object? Value { get; set; } = true;
        public Instant? ExpiredAt { get; set; }
        public Instant? AffectedAt { get; set; }
    }

    [HttpGet("groups")]
    [AskPermission("permissions.groups.check")]
    public async Task<ActionResult<List<PermissionGroupSummary>>> ListGroups(
        [FromQuery] string? query = null,
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        take = Math.Clamp(take, 1, 200);
        offset = Math.Max(offset, 0);

        var groups = db.PermissionGroups.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var probe = query.Trim();
            groups = groups.Where(g => EF.Functions.ILike(g.Key, $"%{probe}%"));
        }

        var total = await groups.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        return Ok(await groups
            .OrderBy(g => g.Key)
            .Skip(offset)
            .Take(take)
            .Select(g => new PermissionGroupSummary
            {
                Id = g.Id,
                Key = g.Key,
                NodeCount = g.Nodes.Count,
                MemberCount = g.Members.Count,
                CreatedAt = g.CreatedAt,
                UpdatedAt = g.UpdatedAt
            })
            .ToListAsync());
    }

    [HttpGet("groups/{groupId:guid}")]
    [AskPermission("permissions.groups.check")]
    public async Task<ActionResult<PermissionGroupDetailResponse>> GetGroup(Guid groupId)
    {
        var group = await db.PermissionGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return NotFound(new ApiError { Code = "PERMISSION_GROUP_NOT_FOUND", Message = "Permission group not found.", Status = 404 });

        var response = new PermissionGroupDetailResponse
        {
            Group = group,
            Nodes = await db.PermissionNodes.AsNoTracking()
                .Where(n => n.GroupId == groupId)
                .OrderBy(n => n.Key)
                .ToListAsync(),
            Members = await db.PermissionGroupMembers.AsNoTracking()
                .Where(m => m.GroupId == groupId)
                .OrderBy(m => m.Actor)
                .ToListAsync()
        };
        return Ok(response);
    }

    [HttpPost("groups")]
    [AskPermission("permissions.groups.manage")]
    public async Task<ActionResult<SnPermissionGroup>> CreateGroup([FromBody] CreatePermissionGroupRequest request)
    {
        var key = request.Key.Trim();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new ApiError { Code = "PERMISSION_GROUP_KEY_EMPTY", Message = "Group key cannot be empty.", Status = 400 });
        if (await db.PermissionGroups.AnyAsync(g => g.Key == key))
            return Conflict(ApiError.Conflict("A permission group with this key already exists.", code: "PERMISSION_GROUP_KEY_CONFLICT"));

        var group = new SnPermissionGroup { Key = key };
        db.PermissionGroups.Add(group);
        await db.SaveChangesAsync();
        return Created($"/api/admin/permissions/groups/{group.Id}", group);
    }

    [HttpPatch("groups/{groupId:guid}")]
    [AskPermission("permissions.groups.manage")]
    public async Task<ActionResult<SnPermissionGroup>> UpdateGroup(Guid groupId, [FromBody] UpdatePermissionGroupRequest request)
    {
        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return NotFound(new ApiError { Code = "PERMISSION_GROUP_NOT_FOUND", Message = "Permission group not found.", Status = 404 });

        var key = request.Key.Trim();
        if (string.IsNullOrWhiteSpace(key)) return BadRequest(new ApiError { Code = "PERMISSION_GROUP_KEY_EMPTY", Message = "Group key cannot be empty.", Status = 400 });
        if (group.Key == "default" && key != "default")
            return BadRequest(new ApiError { Code = "PERMISSION_GROUP_DEFAULT_RENAME", Message = "The default permission group cannot be renamed.", Status = 400 });
        if (await db.PermissionGroups.AnyAsync(g => g.Id != groupId && g.Key == key))
            return Conflict(ApiError.Conflict("A permission group with this key already exists.", code: "PERMISSION_GROUP_KEY_CONFLICT"));

        group.Key = key;
        var nodes = await db.PermissionNodes.Where(n => n.GroupId == groupId).ToListAsync();
        foreach (var node in nodes) node.Actor = $"group:{key}";
        await db.SaveChangesAsync();
        await ClearGroupMemberCachesAsync(groupId);
        return Ok(group);
    }

    [HttpDelete("groups/{groupId:guid}")]
    [AskPermission("permissions.groups.manage")]
    public async Task<IActionResult> DeleteGroup(Guid groupId)
    {
        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return NotFound(new ApiError { Code = "PERMISSION_GROUP_NOT_FOUND", Message = "Permission group not found.", Status = 404 });
        if (group.Key == "default") return BadRequest(new ApiError { Code = "PERMISSION_GROUP_DEFAULT_DELETE", Message = "The default permission group cannot be deleted.", Status = 400 });

        var actors = await GetGroupMemberActorsAsync(groupId);
        var nodes = await db.PermissionNodes.Where(n => n.GroupId == groupId).ToListAsync();
        var members = await db.PermissionGroupMembers.Where(m => m.GroupId == groupId).ToListAsync();
        db.PermissionNodes.RemoveRange(nodes);
        db.PermissionGroupMembers.RemoveRange(members);
        db.PermissionGroups.Remove(group);
        await db.SaveChangesAsync();
        await ClearActorCachesAsync(actors);
        return NoContent();
    }

    [HttpPut("groups/{groupId:guid}/permissions/{key}")]
    [AskPermission("permissions.manage")]
    [AskPermission("permissions.groups.manage")]
    public async Task<ActionResult<SnPermissionNode>> UpsertGroupPermission(
        Guid groupId,
        string key,
        [FromBody] UpsertGroupPermissionRequest request)
    {
        var group = await db.PermissionGroups.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group is null) return NotFound(new ApiError { Code = "PERMISSION_GROUP_NOT_FOUND", Message = "Permission group not found.", Status = 404 });
        if (!PermissionService.IsValidPermissionPattern(key))
            return BadRequest(new ApiError { Code = "PERMISSION_KEY_INVALID_PATTERN", Message = "Permission key contains invalid characters or wildcards.", Status = 400 });

        var node = await db.PermissionNodes.FirstOrDefaultAsync(n => n.GroupId == groupId && n.Key == key);
        if (node is null)
        {
            node = await permissionService.AddPermissionNodeToGroup(
                group,
                $"group:{group.Key}",
                key,
                JsonDocument.Parse(JsonSerializer.Serialize(request.Value)),
                request.ExpiredAt,
                request.AffectedAt,
                PermissionNodeActorType.Group);
        }
        else
        {
            node.Value = JsonDocument.Parse(JsonSerializer.Serialize(request.Value));
            node.ExpiredAt = request.ExpiredAt;
            node.AffectedAt = request.AffectedAt;
            await db.SaveChangesAsync();
        }

        await ClearGroupMemberCachesAsync(groupId);
        return Ok(node);
    }

    [HttpDelete("groups/{groupId:guid}/permissions/{key}")]
    [AskPermission("permissions.manage")]
    [AskPermission("permissions.groups.manage")]
    public async Task<IActionResult> DeleteGroupPermission(Guid groupId, string key)
    {
        var node = await db.PermissionNodes.FirstOrDefaultAsync(n => n.GroupId == groupId && n.Key == key);
        if (node is null) return NotFound(new ApiError { Code = "PERMISSION_NODE_NOT_FOUND", Message = "Permission node not found.", Status = 404 });

        db.PermissionNodes.Remove(node);
        await db.SaveChangesAsync();
        await ClearGroupMemberCachesAsync(groupId);
        return NoContent();
    }

    [HttpPut("groups/{groupId:guid}/members/{actor}")]
    [AskPermission("permissions.groups.manage")]
    public async Task<ActionResult<SnPermissionGroupMember>> UpsertGroupMember(
        Guid groupId,
        string actor,
        [FromBody] GroupMembershipRequest request)
    {
        var groupExists = await db.PermissionGroups.AnyAsync(g => g.Id == groupId);
        if (!groupExists) return NotFound(new ApiError { Code = "PERMISSION_GROUP_NOT_FOUND", Message = "Permission group not found.", Status = 404 });
        if (string.IsNullOrWhiteSpace(actor)) return BadRequest(new ApiError { Code = "PERMISSION_ACTOR_EMPTY", Message = "Actor cannot be empty.", Status = 400 });

        var member = await db.PermissionGroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.Actor == actor);
        if (member is null)
        {
            member = new SnPermissionGroupMember { GroupId = groupId, Actor = actor, ExpiredAt = request.ExpiredAt, AffectedAt = request.AffectedAt };
            db.PermissionGroupMembers.Add(member);
        }
        else
        {
            member.ExpiredAt = request.ExpiredAt;
            member.AffectedAt = request.AffectedAt;
        }

        await db.SaveChangesAsync();
        await permissionService.ClearActorCacheAsync(actor);
        return Ok(member);
    }

    [HttpDelete("groups/{groupId:guid}/members/{actor}")]
    [AskPermission("permissions.groups.manage")]
    public async Task<IActionResult> DeleteGroupMember(Guid groupId, string actor)
    {
        var member = await db.PermissionGroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.Actor == actor);
        if (member is null) return NotFound(new ApiError { Code = "PERMISSION_ACTOR_NOT_IN_GROUP", Message = "Actor is not in this group.", Status = 404 });

        db.PermissionGroupMembers.Remove(member);
        await db.SaveChangesAsync();
        await permissionService.ClearActorCacheAsync(actor);
        return NoContent();
    }

    [HttpGet("actors/{actor}")]
    [AskPermission("permissions.check")]
    [AskPermission("permissions.groups.check")]
    public async Task<ActionResult<AdminActorPermissionsResponse>> GetActorPermissions(string actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return BadRequest(new ApiError { Code = "PERMISSION_ACTOR_EMPTY", Message = "Actor cannot be empty.", Status = 400 });
        return Ok(new AdminActorPermissionsResponse
        {
            Actor = actor,
            DirectPermissions = await permissionService.ListDirectPermissionsAsync(actor),
            EffectivePermissions = await permissionService.ListEffectivePermissionsAsync(actor),
            Groups = await db.PermissionGroupMembers.AsNoTracking()
                .Where(m => m.Actor == actor)
                .Include(m => m.Group)
                .OrderBy(m => m.Group.Key)
                .ToListAsync()
        });
    }

    private async Task<List<string>> GetGroupMemberActorsAsync(Guid groupId) =>
        await db.PermissionGroupMembers.Where(m => m.GroupId == groupId).Select(m => m.Actor).ToListAsync();

    private async Task ClearGroupMemberCachesAsync(Guid groupId) =>
        await ClearActorCachesAsync(await GetGroupMemberActorsAsync(groupId));

    private async Task ClearActorCachesAsync(IEnumerable<string> actors)
    {
        foreach (var actor in actors.Distinct())
            await permissionService.ClearActorCacheAsync(actor);
    }
}
