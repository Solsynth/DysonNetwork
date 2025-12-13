using System.Diagnostics.CodeAnalysis;
using dotnet_etcd;
using Microsoft.Extensions.ServiceDiscovery;

namespace DysonNetwork.Shared.Registry;

/// <summary>
/// A factory for creating <see cref="RegistrarServiceEndpointProvider"/> instances.
/// </summary>
public class RegistrarServiceEndpointFactory(EtcdClient etcdClient) : IServiceEndpointProviderFactory
{
    /// <summary>
    /// Tries to create a provider for the given query.
    /// </summary>
    /// <remarks>
    /// This factory creates a provider for any service name. It supports a convention
    /// where the service name can include the service part, e.g., "my-service.http" or "my-service.grpc".
    /// If the service part is not specified, it defaults to "http".
    /// </remarks>
    public bool TryCreateProvider(ServiceEndpointQuery query, [NotNullWhen(true)] out IServiceEndpointProvider? provider)
    {
        var serviceName = query.ServiceName;
        var servicePart = "grpc"; // Default to "grpc"

        var lastDot = serviceName.LastIndexOf('.');
        if (lastDot > 0 && lastDot < serviceName.Length - 1)
        {
            var part = serviceName[(lastDot + 1)..];
            // You might want to have a list of known parts.
            // For now, we assume any suffix after a dot is a service part.
            servicePart = part;
            serviceName = serviceName[..lastDot];
        }

        provider = new RegistrarServiceEndpointProvider(serviceName, servicePart, etcdClient);
        return true;
    }
}
