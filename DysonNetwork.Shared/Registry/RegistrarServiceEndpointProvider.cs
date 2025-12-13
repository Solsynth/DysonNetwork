using System.Net;
using dotnet_etcd;
using Microsoft.Extensions.ServiceDiscovery;

namespace DysonNetwork.Shared.Registry;

/// <summary>
/// A service endpoint provider that resolves endpoints from an etcd registry.
/// </summary>
public class RegistrarServiceEndpointProvider(string serviceName, string servicePart, EtcdClient etcdClient)
    : IServiceEndpointProvider
{
    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Populates the endpoints for the service.
    /// </summary>
    public async ValueTask PopulateAsync(IServiceEndpointBuilder endpoints, CancellationToken cancellationToken)
    {
        var prefix = $"/services/{serviceName}/{servicePart}/";

        // Fetch service instances from etcd.
        var response = await etcdClient.GetRangeAsync(prefix, cancellationToken: cancellationToken);
        var instances = response.Kvs.Select(kv => kv.Value.ToStringUtf8());

        foreach (var instance in instances)
        {
            // Instances are in "host:port" format.
            var parts = instance.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out var port)) continue;
            var host = parts[0];

            // Create a DnsEndPoint. The framework will use the original request's scheme (e.g., http, https).
            endpoints.Endpoints.Add(ServiceEndpoint.Create(new DnsEndPoint(host, port)));
        }
    }
}
