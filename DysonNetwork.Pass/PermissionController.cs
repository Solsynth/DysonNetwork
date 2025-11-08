using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Pass.Permission;
using DysonNetwork.Shared.Models;
using NodaTime;
using System.Text.Json;

namespace DysonNetwork.Pass;

[ApiController]
[Route("/api/permissions")]
[Authorize]
public class PermissionController(
    PermissionService permissionService,
    AppDatabase db
) : ControllerBase
{
    /// <summary>
    /// Check if an actor has a specific permission
    /// </summary>
    [HttpGet("check/{actor}/{area}/{key}")]
    [RequiredPermission("maintenance", "permissions.check")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CheckPermission(string actor, string area, string key)
    {
        try
        {
            var hasPermission = await permissionService.HasPermissionAsync(actor, area, key);
            return Ok(hasPermission);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to check permission", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all effective permissions for an actor (including group permissions)
    /// </summary>
    [HttpGet("actors/{actor}/permissions/effective")]
    [RequiredPermission("maintenance", "permissions.check")]
    [ProducesResponseType<List<SnPermissionNode>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetEffectivePermissions(string actor)
    {
        try
        {
            var permissions = await permissionService.ListEffectivePermissionsAsync(actor);
            return Ok(permissions);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to list permissions", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all direct permissions for an actor (excluding group permissions)
    /// </summary>
    [HttpGet("actors/{actor}/permissions/direct")]
    [RequiredPermission("maintenance", "permissions.check")]
    [ProducesResponseType<List<SnPermissionNode>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetDirectPermissions(string actor)
    {
        try
        {
            var permissions = await permissionService.ListDirectPermissionsAsync(actor);
            return Ok(permissions);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to list permissions", details = ex.Message });
        }
    }

    /// <summary>
    /// Give a permission to an actor
    /// </summary>
    [HttpPost("actors/{actor}/permissions/{area}/{key}")]
    [RequiredPermission("maintenance", "permissions.manage")]
    [ProducesResponseType<SnPermissionNode>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GivePermission(
        string actor,
        string area,
        string key,
        [FromBody] PermissionRequest request)
    {
        try
        {
            var permission = await permissionService.AddPermissionNode(
                actor,
                area,
                key,
                JsonDocument.Parse(JsonSerializer.Serialize(request.Value)),
                request.ExpiredAt,
                request.AffectedAt
            );
            return Created($"/api/permissions/actors/{actor}/permissions/{area}/{key}", permission);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to add permission", details = ex.Message });
        }
    }

    /// <summary>
    /// Remove a permission from an actor
    /// </summary>
    [HttpDelete("actors/{actor}/permissions/{area}/{key}")]
    [RequiredPermission("maintenance", "permissions.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RemovePermission(string actor, string area, string key)
    {
        try
        {
            await permissionService.RemovePermissionNode(actor, area, key);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to remove permission", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all groups for an actor
    /// </summary>
    [HttpGet("actors/{actor}/groups")]
    [RequiredPermission("maintenance", "permissions.groups.check")]
    [ProducesResponseType<List<SnPermissionGroupMember>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetActorGroups(string actor)
    {
        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var groups = await db.PermissionGroupMembers
                .Where(m => m.Actor == actor)
                .Where(m => m.ExpiredAt == null || m.ExpiredAt > now)
                .Where(m => m.AffectedAt == null || m.AffectedAt <= now)
                .Include(m => m.Group)
                .ToListAsync();

            return Ok(groups);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to list actor groups", details = ex.Message });
        }
    }

    /// <summary>
    /// Add an actor to a permission group
    /// </summary>
    [HttpPost("actors/{actor}/groups/{groupId}")]
    [RequiredPermission("maintenance", "permissions.groups.manage")]
    [ProducesResponseType<SnPermissionGroupMember>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AddActorToGroup(
        string actor,
        Guid groupId,
        [FromBody] GroupMembershipRequest? request = null)
    {
        try
        {
            var group = await db.PermissionGroups.FindAsync(groupId);
            if (group == null)
            {
                return NotFound(new { error = "Permission group not found" });
            }

            // Check if actor is already in the group
            var existing = await db.PermissionGroupMembers
                .FirstOrDefaultAsync(m => m.Actor == actor && m.GroupId == groupId);

            if (existing != null)
            {
                return BadRequest(new { error = "Actor is already in this group" });
            }

            var member = new SnPermissionGroupMember
            {
                Actor = actor,
                GroupId = groupId,
                Group = group,
                ExpiredAt = request?.ExpiredAt,
                AffectedAt = request?.AffectedAt
            };

            db.PermissionGroupMembers.Add(member);
            await db.SaveChangesAsync();

            // Clear actor cache
            await permissionService.ClearActorCacheAsync(actor);

            return Created($"/api/permissions/actors/{actor}/groups/{groupId}", member);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to add actor to group", details = ex.Message });
        }
    }

    /// <summary>
    /// Remove an actor from a permission group
    /// </summary>
    [HttpDelete("actors/{actor}/groups/{groupId}")]
    [RequiredPermission("maintenance", "permissions.groups.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RemoveActorFromGroup(string actor, Guid groupId)
    {
        try
        {
            var member = await db.PermissionGroupMembers
                .FirstOrDefaultAsync(m => m.Actor == actor && m.GroupId == groupId);

            if (member == null)
            {
                return NotFound(new { error = "Actor is not in this group" });
            }

            db.PermissionGroupMembers.Remove(member);
            await db.SaveChangesAsync();

            // Clear actor cache
            await permissionService.ClearActorCacheAsync(actor);

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to remove actor from group", details = ex.Message });
        }
    }

    /// <summary>
    /// Clear permission cache for an actor
    /// </summary>
    [HttpPost("actors/{actor}/cache/clear")]
    [RequiredPermission("maintenance", "permissions.cache.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ClearActorCache(string actor)
    {
        try
        {
            await permissionService.ClearActorCacheAsync(actor);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Failed to clear cache", details = ex.Message });
        }
    }

    /// <summary>
    /// Validate a permission pattern
    /// </summary>
    [HttpPost("validate-pattern")]
    [RequiredPermission("maintenance", "permissions.check")]
    [ProducesResponseType<PatternValidationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult ValidatePattern([FromBody] PatternValidationRequest request)
    {
        try
        {
            var isValid = PermissionService.IsValidPermissionPattern(request.Pattern);
            return Ok(new PatternValidationResponse
            {
                Pattern = request.Pattern,
                IsValid = isValid,
                Message = isValid ? "Pattern is valid" : "Pattern contains invalid characters or consecutive wildcards"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public class PermissionRequest
{
    public object? Value { get; set; }
    public NodaTime.Instant? ExpiredAt { get; set; }
    public NodaTime.Instant? AffectedAt { get; set; }
}

public class GroupMembershipRequest
{
    public NodaTime.Instant? ExpiredAt { get; set; }
    public NodaTime.Instant? AffectedAt { get; set; }
}

public class PatternValidationRequest
{
    public string Pattern { get; set; } = string.Empty;
}

public class PatternValidationResponse
{
    public string Pattern { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
}
