using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Pass;

[ApiController]
[Route("/api/ip-check")]
public class IpCheckController : ControllerBase
{
    public class IpCheckResponse
    {
        public string? RemoteIp { get; set; }
        public string? XForwardedFor { get; set; }
        public string? XForwardedProto { get; set; }
        public string? XForwardedHost { get; set; }
        public string? XRealIp { get; set; }
        public string? Headers { get; set; }
    }
    
    [HttpGet]
    public ActionResult<IpCheckResponse> GetIpCheck()
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var xForwardedProto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var xForwardedHost = Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();

        return Ok(new IpCheckResponse
        {
            RemoteIp = ip,
            XForwardedFor = xForwardedFor,
            XForwardedProto = xForwardedProto,
            XForwardedHost = xForwardedHost,
            XRealIp = realIp,
            Headers = string.Join('\n', Request.Headers.Select(h => $"{h.Key}: {h.Value}")),
        });
    } 
}