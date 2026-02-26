using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Drive.Billing;

[ApiController]
[Route("api/billing/usage")]
public class UsageController(UsageService usage, QuotaService quota, ICacheService cache) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<TotalUsageDetails>> GetTotalUsage()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        
        var cacheKey = $"file:usage:{accountId}";
        
        // Try to get from cache first
        var (found, cachedResult) = await cache.GetAsyncWithStatus<TotalUsageDetails>(cacheKey);
        if (found && cachedResult != null)
            return Ok(cachedResult);

        // If not in cache, get from services
        var result = await usage.GetTotalUsage(accountId);
        var totalQuota = await quota.GetQuota(accountId);
        result.TotalQuota = totalQuota;

        // Cache the result for 5 minutes
        await cache.SetAsync(cacheKey, result, TimeSpan.FromMinutes(5));

        return Ok(result);
    }

    [Authorize]
    [HttpGet("{poolId:guid}")]
    public async Task<ActionResult<UsageDetails>> GetPoolUsage(Guid poolId)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);
        
        var usageDetails = await usage.GetPoolUsage(poolId, accountId);
        if (usageDetails == null)
            return NotFound();
        return usageDetails;
    }
}
