using System.Text;
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
                serviceMap[key] = domain;
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