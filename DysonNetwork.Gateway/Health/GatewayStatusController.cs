using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Gateway.Health;

[ApiController]
[Route("/health")]
public class GatewayStatusController(GatewayReadinessStore readinessStore) : ControllerBase
{
    [HttpGet]
    public ActionResult<GatewayReadinessState> GetHealthStatus()
    {
        return Ok(readinessStore.Current);
    }
}