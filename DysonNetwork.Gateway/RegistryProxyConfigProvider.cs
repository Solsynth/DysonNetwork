using System.Text;
using dotnet_etcd.interfaces;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

namespace DysonNetwork.Gateway;

public class RegistryProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly object _lock = new();
    private readonly IEtcdClient _etcdClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegistryProxyConfigProvider> _logger;
    private readonly CancellationTokenSource _watchCts = new();
    private CancellationTokenSource _cts;
    private IProxyConfig _config;

    public RegistryProxyConfigProvider(
        IEtcdClient etcdClient,
        IConfiguration configuration,
        ILogger<RegistryProxyConfigProvider> logger
    )
    {
        _etcdClient = etcdClient;
        _configuration = configuration;
        _logger = logger;
        _cts = new CancellationTokenSource();
        _config = LoadConfig();

        // Watch for changes in etcd
        _etcdClient.WatchRange("/services/", _ =>
        {
            _logger.LogInformation("Etcd configuration changed. Reloading proxy config.");
            ReloadConfig();
        }, cancellationToken: _watchCts.Token);
    }

    public IProxyConfig GetConfig() => _config;

    private void ReloadConfig()
    {
        lock (_lock)
        {
            var oldCts = _cts;
            _cts = new CancellationTokenSource();
            _config = LoadConfig();
            oldCts.Cancel();
            oldCts.Dispose();
        }
    }

    private IProxyConfig LoadConfig()
    {
        _logger.LogInformation("Generating new proxy config.");
        var response = _etcdClient.GetRange("/services/");
        var kvs = response.Kvs;

        var serviceMap = kvs.ToDictionary(
            kv => Encoding.UTF8.GetString(kv.Key.ToByteArray()).Replace("/services/", ""),
            kv => Encoding.UTF8.GetString(kv.Value.ToByteArray())
        );

        var clusters = new List<ClusterConfig>();
        var routes = new List<RouteConfig>();

        var domainMappings = _configuration.GetSection("DomainMappings").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);

        var pathAliases = _configuration.GetSection("PathAliases").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);

        var directRoutes = _configuration.GetSection("DirectRoutes").Get<List<DirectRouteConfig>>() ??
                           [];

        _logger.LogInformation("Indexing {ServiceCount} services from Etcd.", kvs.Count);

        var gatewayServiceName = _configuration["Service:Name"];

        // Add direct routes
        foreach (var directRoute in directRoutes)
        {
            if (serviceMap.TryGetValue(directRoute.Service, out var serviceUrl))
            {
                var existingCluster = clusters.FirstOrDefault(c => c.ClusterId == directRoute.Service);
                if (existingCluster is null)
                {
                    var cluster = new ClusterConfig
                    {
                        ClusterId = directRoute.Service,
                        Destinations = new Dictionary<string, DestinationConfig>
                        {
                            { "destination1", new DestinationConfig { Address = serviceUrl } }
                        },
                    };
                    clusters.Add(cluster);
                }

                var route = new RouteConfig
                {
                    RouteId = $"direct-{directRoute.Service}-{directRoute.Path.Replace("/", "-")}",
                    ClusterId = directRoute.Service,
                    Match = new RouteMatch { Path = directRoute.Path },
                };
                routes.Add(route);
                _logger.LogInformation("    Added Direct Route: {Path} -> {Service}", directRoute.Path,
                    directRoute.Service);
            }
            else
            {
                _logger.LogWarning("    Direct route service {Service} not found in Etcd.", directRoute.Service);
            }
        }

        foreach (var serviceName in serviceMap.Keys)
        {
            if (serviceName == gatewayServiceName)
            {
                _logger.LogInformation("Skipping gateway service: {ServiceName}", serviceName);
                continue;
            }

            var serviceUrl = serviceMap[serviceName];

            // Determine the path alias
            string? pathAlias;
            pathAlias = pathAliases.TryGetValue(serviceName, out var alias)
                ? alias
                : serviceName.Split('.').Last().ToLowerInvariant();

            _logger.LogInformation("  Service: {ServiceName}, URL: {ServiceUrl}, Path Alias: {PathAlias}", serviceName,
                serviceUrl, pathAlias);

            // Check if the cluster already exists
            var existingCluster = clusters.FirstOrDefault(c => c.ClusterId == serviceName);
            if (existingCluster == null)
            {
                var cluster = new ClusterConfig
                {
                    ClusterId = serviceName,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        { "destination1", new DestinationConfig { Address = serviceUrl } }
                    }
                };
                clusters.Add(cluster);
                _logger.LogInformation("  Added Cluster: {ServiceName}", serviceName);
            }
            else if (existingCluster.Destinations is not null)
            {
                // Create a new cluster with merged destinations
                var newDestinations = new Dictionary<string, DestinationConfig>(existingCluster.Destinations)
                {
                    {
                        $"destination{existingCluster.Destinations.Count + 1}",
                        new DestinationConfig { Address = serviceUrl }
                    }
                };

                var mergedCluster = new ClusterConfig
                {
                    ClusterId = serviceName,
                    Destinations = newDestinations
                };

                // Replace the existing cluster with the merged one
                var index = clusters.IndexOf(existingCluster);
                clusters[index] = mergedCluster;

                _logger.LogInformation("  Updated Cluster {ServiceName} with {DestinationCount} destinations",
                    serviceName, mergedCluster.Destinations.Count);
            }

            // Host-based routing
            if (domainMappings.TryGetValue(serviceName, out var domain) && domain is not null)
            {
                var hostRoute = new RouteConfig
                {
                    RouteId = $"{serviceName}-host",
                    ClusterId = serviceName,
                    Match = new RouteMatch
                    {
                        Hosts = [domain],
                        Path = "/{**catch-all}"
                    },
                };
                routes.Add(hostRoute);
                _logger.LogInformation("    Added Host-based Route: {Host}", domain);
            }

            // Path-based routing
            var pathRoute = new RouteConfig
            {
                RouteId = $"{serviceName}-path",
                ClusterId = serviceName,
                Match = new RouteMatch { Path = $"/{pathAlias}/{{**catch-all}}" },
                Transforms = new List<Dictionary<string, string>>
                {
                    new() { { "PathRemovePrefix", $"/{pathAlias}" } },
                    new() { { "PathPrefix", "/api" } },
                },
                Timeout = TimeSpan.FromSeconds(5)
            };
            routes.Add(pathRoute);
            _logger.LogInformation("    Added Path-based Route: {Path}", pathRoute.Match.Path);
        }

        return new CustomProxyConfig(
            routes,
            clusters,
            new Microsoft.Extensions.Primitives.CancellationChangeToken(_cts.Token)
        );
    }

    private class CustomProxyConfig(
        IReadOnlyList<RouteConfig> routes,
        IReadOnlyList<ClusterConfig> clusters,
        Microsoft.Extensions.Primitives.IChangeToken changeToken
    )
        : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public Microsoft.Extensions.Primitives.IChangeToken ChangeToken { get; } = changeToken;
    }

    public record DirectRouteConfig
    {
        public required string Path { get; set; }
        public required string Service { get; set; }
    }

    public virtual void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _watchCts.Cancel();
        _watchCts.Dispose();
    }
}