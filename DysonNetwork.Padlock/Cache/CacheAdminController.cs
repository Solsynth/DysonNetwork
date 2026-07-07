using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Cache;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Padlock.Cache;

[ApiController]
[Route("/api/admin/cache")]
[Authorize]
public class CacheAdminController(
    ICacheService cache,
    ILogger<CacheAdminController> logger
) : ControllerBase
{
    public class ClearKeyRequest
    {
        [Required]
        [MaxLength(1024)]
        public string Key { get; set; } = string.Empty;
    }

    public class ClearGroupRequest
    {
        [Required]
        [MaxLength(1024)]
        public string Group { get; set; } = string.Empty;
    }

    public class CacheGroupResponse
    {
        public string Group { get; set; } = string.Empty;
        public int Count { get; set; }
        public List<string> Keys { get; set; } = [];
    }

    public class CacheClearResponse
    {
        public string Scope { get; set; } = string.Empty;
        public string? Key { get; set; }
        public string? Group { get; set; }
        public long RemovedCount { get; set; }
    }

    [HttpGet("stats")]
    [AskPermission(PermissionKeys.PermissionsCacheManage)]
    [ProducesResponseType<CacheStatsSnapshot>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CacheStatsSnapshot>> GetStats()
    {
        var stats = await cache.GetStatsAsync();
        return Ok(stats);
    }

    [HttpGet("groups/{group}")]
    [AskPermission(PermissionKeys.PermissionsCacheManage)]
    [ProducesResponseType<CacheGroupResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CacheGroupResponse>> GetGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group))
            return BadRequest(new { error = "Group is required." });

        var keys = (await cache.GetGroupKeysAsync(group)).Order().ToList();
        return Ok(new CacheGroupResponse
        {
            Group = group,
            Count = keys.Count,
            Keys = keys
        });
    }

    [HttpPost("keys/clear")]
    [AskPermission(PermissionKeys.PermissionsCacheManage)]
    [ProducesResponseType<CacheClearResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CacheClearResponse>> ClearKey([FromBody] ClearKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest(new { error = "Key is required." });

        await cache.RemoveAsync(request.Key.Trim());
        logger.LogWarning("Admin cleared cache key {Key}", request.Key.Trim());
        return Ok(new CacheClearResponse
        {
            Scope = "key",
            Key = request.Key.Trim(),
            RemovedCount = 1
        });
    }

    [HttpPost("groups/clear")]
    [AskPermission(PermissionKeys.PermissionsCacheManage)]
    [ProducesResponseType<CacheClearResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CacheClearResponse>> ClearGroup([FromBody] ClearGroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Group))
            return BadRequest(new { error = "Group is required." });

        var group = request.Group.Trim();
        var removedCount = (await cache.GetGroupKeysAsync(group)).LongCount();
        await cache.RemoveGroupAsync(group);
        logger.LogWarning("Admin cleared cache group {Group} with {Count} keys", group, removedCount);
        return Ok(new CacheClearResponse
        {
            Scope = "group",
            Group = group,
            RemovedCount = removedCount
        });
    }

    [HttpPost("clear")]
    [AskPermission(PermissionKeys.PermissionsCacheManage)]
    [ProducesResponseType<CacheClearResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CacheClearResponse>> ClearAll()
    {
        var removedCount = await cache.ClearAllAsync();
        logger.LogWarning("Admin cleared all Dyson cache entries. Removed {Count} keys", removedCount);
        return Ok(new CacheClearResponse
        {
            Scope = "all",
            RemovedCount = removedCount
        });
    }
}
