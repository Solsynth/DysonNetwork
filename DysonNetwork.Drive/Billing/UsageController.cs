using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Drive.Billing;

[ApiController]
[Route("api/billing/usage")]
public class UsageController(UsageService usageService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TotalUsageDetails>> GetTotalUsage()
    {
        return await usageService.GetTotalUsage();
    }

    [HttpGet("{poolId:guid}")]
    public async Task<ActionResult<UsageDetails>> GetPoolUsage(Guid poolId)
    {
        var usageDetails = await usageService.GetPoolUsage(poolId);
        if (usageDetails == null)
        {
            return NotFound();
        }
        return usageDetails;
    }
}
