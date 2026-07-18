using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Shared.Models;
using NodaTime;
using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Padlock.Permission;

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
    [HttpGet("check/{actor}/{key}")]
    [AskPermission("permissions.check")]
    [ProducesResponseType<bool>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CheckPermission(
        [FromRoute] string actor,
        [FromRoute] string key,
        [FromQuery] PermissionNodeActorType type = PermissionNodeActorType.Account
    )
    {
        try
        {
            var hasPermission = await permissionService.HasPermissionAsync(actor, key, type);
            return Ok(hasPermission);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError { Code = "PERMISSION_CHECK_INVALID_INPUT", Message = ex.Message, Status = 400 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "PERMISSION_CHECK_ERROR", Message = "Failed to check permission.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Get all effective permissions for an actor (including group permissions)
    /// </summary>
    [HttpGet("actors/{actor}/permissions/effective")]
    [AskPermission("permissions.check")]
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
            return BadRequest(new ApiError { Code = "PERMISSION_LIST_INVALID_INPUT", Message = ex.Message, Status = 400 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "PERMISSION_LIST_ERROR", Message = "Failed to list permissions.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Get all direct permissions for an actor (excluding group permissions)
    /// </summary>
    [HttpGet("actors/{actor}/permissions/direct")]
    [AskPermission("permissions.check")]
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
            return BadRequest(new ApiError { Code = "PERMISSION_LIST_INVALID_INPUT", Message = ex.Message, Status = 400 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "PERMISSION_LIST_ERROR", Message = "Failed to list permissions.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Give a permission to an actor
    /// </summary>
    [HttpPost("actors/{actor}/permissions/{key}")]
    [AskPermission("permissions.manage")]
    [ProducesResponseType<SnPermissionNode>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GivePermission(
        string actor,
        string key,
        [FromBody] PermissionRequest request
    )
    {
        try
        {
            var permission = await permissionService.AddPermissionNode(
                actor,
                key,
                JsonDocument.Parse(JsonSerializer.Serialize(request.Value)),
                request.ExpiredAt,
                request.AffectedAt
            );
            return Created($"/api/permissions/actors/{actor}/permissions/{key}", permission);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError { Code = "PERMISSION_ADD_INVALID_INPUT", Message = ex.Message, Status = 400 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "PERMISSION_ADD_ERROR", Message = "Failed to add permission.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Remove a permission from an actor
    /// </summary>
    [HttpDelete("actors/{actor}/permissions/{key}")]
    [AskPermission("permissions.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RemovePermission(
        string actor,
        string key,
        [FromQuery] PermissionNodeActorType type = PermissionNodeActorType.Account
    )
    {
        try
        {
            await permissionService.RemovePermissionNode(actor, key, type);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError { Code = "PERMISSION_REMOVE_INVALID_INPUT", Message = ex.Message, Status = 400 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "PERMISSION_REMOVE_ERROR", Message = "Failed to remove permission.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Get all groups for an actor
    /// </summary>
    [HttpGet("actors/{actor}/groups")]
    [AskPermission("permissions.groups.check")]
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
            return StatusCode(500, new ApiError { Code = "PERMISSION_ACTOR_GROUPS_ERROR", Message = "Failed to list actor groups.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Add an actor to a permission group
    /// </summary>
    [HttpPost("actors/{actor}/groups/{groupId:guid}")]
    [AskPermission("permissions.groups.manage")]
    [ProducesResponseType<SnPermissionGroupMember>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AddActorToGroup(
        string actor,
        Guid groupId,
        [FromBody] GroupMembershipRequest? request = null
    )
    {
        try
        {
            var group = await db.PermissionGroups.FindAsync(groupId);
            if (group == null)
            {
                return NotFound(new ApiError { Code = "PERMISSION_GROUP_NOT_FOUND", Message = "Permission group not found.", Status = 404 });
            }

            // Check if actor is already in the group
            var existing = await db.PermissionGroupMembers
                .FirstOrDefaultAsync(m => m.Actor == actor && m.GroupId == groupId);

            if (existing != null)
            {
                return BadRequest(new ApiError { Code = "PERMISSION_ACTOR_ALREADY_IN_GROUP", Message = "Actor is already in this group.", Status = 400 });
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
            return StatusCode(500, new ApiError { Code = "PERMISSION_ADD_ACTOR_TO_GROUP_ERROR", Message = "Failed to add actor to group.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Remove an actor from a permission group
    /// </summary>
    [HttpDelete("actors/{actor}/groups/{groupId}")]
    [AskPermission("permissions.groups.manage")]
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
                return NotFound(new ApiError { Code = "PERMISSION_ACTOR_NOT_IN_GROUP", Message = "Actor is not in this group.", Status = 404 });
            }

            db.PermissionGroupMembers.Remove(member);
            await db.SaveChangesAsync();

            // Clear actor cache
            await permissionService.ClearActorCacheAsync(actor);

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "PERMISSION_REMOVE_ACTOR_FROM_GROUP_ERROR", Message = "Failed to remove actor from group.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Clear permission cache for an actor
    /// </summary>
    [HttpPost("actors/{actor}/cache/clear")]
    [AskPermission("permissions.cache.manage")]
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
            return BadRequest(new ApiError { Code = "PERMISSION_CLEAR_CACHE_INVALID_INPUT", Message = ex.Message, Status = 400 });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiError { Code = "PERMISSION_CLEAR_CACHE_ERROR", Message = "Failed to clear cache.", Status = 500, Detail = ex.Message });
        }
    }

    /// <summary>
    /// Validate a permission pattern
    /// </summary>
    [HttpPost("validate-pattern")]
    [AskPermission("permissions.check")]
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
            return BadRequest(new ApiError { Code = "PERMISSION_VALIDATE_PATTERN_ERROR", Message = ex.Message, Status = 400 });
        }
    }
}

public class PermissionRequest
{
    public object? Value { get; set; }
    public Instant? ExpiredAt { get; set; }
    public Instant? AffectedAt { get; set; }
}

public class GroupMembershipRequest
{
    public Instant? ExpiredAt { get; set; }
    public Instant? AffectedAt { get; set; }
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