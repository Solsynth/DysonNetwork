using System.Text;
using System.Text.Json.Serialization;
using dotnet_etcd.interfaces;
using Microsoft.AspNetCore.Mvc;
using Yarp.ReverseProxy.Configuration;

namespace DysonNetwork.Gateway.Controllers;

[ApiController]
[Route("/.well-known")]
public class WellKnownController(
    IConfiguration configuration,
    IProxyConfigProvider proxyConfigProvider,
    IEtcdClient etcdClient)
    : ControllerBase
{
    public class IpCheckResponse
    {
        [JsonPropertyName("remote_ip")] public string? RemoteIp { get; set; }
        [JsonPropertyName("x_forwarded_for")] public string? XForwardedFor { get; set; }
        [JsonPropertyName("x_forwarded_proto")] public string? XForwardedProto { get; set; }
        [JsonPropertyName("x_forwarded_host")] public string? XForwardedHost { get; set; }
        [JsonPropertyName("x_real_ip")] public string? XRealIp { get; set; }
    }
    
    [HttpGet("ip-check")]
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
            XRealIp = realIp
        });
    }
    
    [HttpGet("domains")]
    public IActionResult GetDomainMappings()
    {
        var domainMappings = configuration.GetSection("DomainMappings").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);
        return Ok(domainMappings);
    }

    [HttpGet("services")]
    public IActionResult GetServices()
    {
        var local = configuration.GetValue<bool>("LocalMode");
        var response = etcdClient.GetRange("/services/");
        var kvs = response.Kvs;

        var serviceMap = kvs.ToDictionary(
            kv => Encoding.UTF8.GetString(kv.Key.ToByteArray()).Replace("/services/", ""),
            kv => Encoding.UTF8.GetString(kv.Value.ToByteArray())
        );

        if (local) return Ok(serviceMap);
        
        var domainMappings = configuration.GetSection("DomainMappings").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);
        foreach (var (key, _) in serviceMap.ToList())
        {
            if (!domainMappings.TryGetValue(key, out var domain)) continue;
            if (domain is not null)
                serviceMap[key] = "http://" + domain;
        }

        return Ok(serviceMap);
    }

    [HttpGet("routes")]
    public IActionResult GetProxyRules()
    {
        var config = proxyConfigProvider.GetConfig();
        var rules = config.Routes.Select(r => new
        {
            r.RouteId,
            r.ClusterId,
            Match = new
            {
                r.Match.Path,
                Hosts = r.Match.Hosts != null ? string.Join(", ", r.Match.Hosts) : null
            },
            Transforms = r.Transforms?.Select(t => t.Select(kv => $"{kv.Key}: {kv.Value}").ToList())
        }).ToList();

        var clusters = config.Clusters.Select(c => new
        {
            c.ClusterId,
            Destinations = c.Destinations?.Select(d => new
            {
                d.Key,
                d.Value.Address
            }).ToList()
        }).ToList();

        return Ok(new { Rules = rules, Clusters = clusters });
    }
}