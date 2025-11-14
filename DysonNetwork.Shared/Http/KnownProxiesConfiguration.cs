using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using IPNetwork = System.Net.IPNetwork;

namespace DysonNetwork.Shared.Http;

public static class KnownProxiesConfiguration
{
    public static WebApplication ConfigureForwardedHeaders(this WebApplication app, IConfiguration configuration)
    {
        var knownProxiesSection = configuration.GetSection("KnownProxies");
        var forwardedHeadersOptions = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.All };

        if (knownProxiesSection.Exists())
        {
            var proxyAddresses = knownProxiesSection.Get<string[]>();
            if (proxyAddresses != null)
            {
                foreach (var proxy in proxyAddresses)
                {
                    if (IPAddress.TryParse(proxy, out var ipAddress))
                        forwardedHeadersOptions.KnownProxies.Add(ipAddress);
                    else if (IPNetwork.TryParse(proxy, out var ipNetwork))
                        forwardedHeadersOptions.KnownIPNetworks.Add(ipNetwork);
                }
            }
        }

        if (forwardedHeadersOptions.KnownProxies.Count == 0 && forwardedHeadersOptions.KnownIPNetworks.Count == 0)
        {
            forwardedHeadersOptions.KnownProxies.Add(IPAddress.Any);
            forwardedHeadersOptions.KnownProxies.Add(IPAddress.IPv6Any);
        }

        app.UseForwardedHeaders(forwardedHeadersOptions);

        return app;
    }
}