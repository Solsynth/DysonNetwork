using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Develop.Identity;

[ApiController]
[Route("/api/develop/quota")]
[Authorize]
public class DevelopQuotaController(
    DeveloperQuotaService quotaService,
    RemoteAccountService remoteAccounts
) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ResourceQuotaResponse<DeveloperBotQuotaRecord>>> GetQuota()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var account = SnAccount.FromProtoValue(await remoteAccounts.GetAccount(Guid.Parse(currentUser.Id)));
        return Ok(await quotaService.GetQuotaAsync(account));
    }
}
