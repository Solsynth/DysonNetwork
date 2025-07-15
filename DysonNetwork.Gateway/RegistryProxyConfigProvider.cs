using System.Text;
using dotnet_etcd.interfaces;
using Yarp.ReverseProxy.Configuration;

namespace DysonNetwork.Gateway;

public class RegistryProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly IEtcdClient _etcdClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RegistryProxyConfigProvider> _logger;
    private readonly CancellationTokenSource _watchCts = new();
    private CancellationTokenSource _cts = new();

    public RegistryProxyConfigProvider(IEtcdClient etcdClient, IConfiguration configuration, ILogger<RegistryProxyConfigProvider> logger)
    {
        _etcdClient = etcdClient;
        _configuration = configuration;
        _logger = logger;

        // Watch for changes in etcd
        _etcdClient.WatchRange("/services/", _ =>
        {
            _logger.LogInformation("Etcd configuration changed. Reloading proxy config.");
            _cts.Cancel();
            _cts = new CancellationTokenSource();
        }, cancellationToken: _watchCts.Token);
    }

    public IProxyConfig GetConfig()
    {
        // This will be called by YARP when it needs a new config
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

        var directRoutes = _configuration.GetSection("DirectRoutes").Get<List<DirectRouteConfig>>() ?? new List<DirectRouteConfig>();

        _logger.LogInformation("Indexing {ServiceCount} services from Etcd.", kvs.Count);

        var gatewayServiceName = _configuration["Service:Name"];

        // Add direct routes
        foreach (var directRoute in directRoutes)
        {
            if (serviceMap.TryGetValue(directRoute.Service, out var serviceUrl))
            {
                var cluster = new ClusterConfig
                {
                    ClusterId = directRoute.Service,
                    Destinations = new Dictionary<string, DestinationConfig>
                    {
                        { "destination1", new DestinationConfig { Address = serviceUrl } }
                    }
                };
                clusters.Add(cluster);

                var route = new RouteConfig
                {
                    RouteId = $"direct-{directRoute.Service}-{directRoute.Path.Replace("/", "-")}",
                    ClusterId = directRoute.Service,
                    Match = new RouteMatch { Path = directRoute.Path }
                };
                routes.Add(route);
                _logger.LogInformation("    Added Direct Route: {Path} -> {Service}", directRoute.Path, directRoute.Service);
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
            string pathAlias;
            if (pathAliases.TryGetValue(serviceName, out var alias))
            {
                pathAlias = alias;
            }
            else
            {
                pathAlias = serviceName.Split('.').Last().ToLowerInvariant();
            }

            _logger.LogInformation("  Service: {ServiceName}, URL: {ServiceUrl}, Path Alias: {PathAlias}", serviceName, serviceUrl, pathAlias);

            var cluster = new ClusterConfig
            {
                ClusterId = serviceName,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    { "destination1", new DestinationConfig { Address = serviceUrl } }
                }
            };
            clusters.Add(cluster);

            // Host-based routing
            if (domainMappings.TryGetValue(serviceName, out var domain))
            {
                var hostRoute = new RouteConfig
                {
                    RouteId = $"{serviceName}-host",
                    ClusterId = serviceName,
                    Match = new RouteMatch
                    {
                        Hosts = new[] { domain },
                        Path = "/{**catch-all}"
                    }
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
                    new Dictionary<string, string> { { "PathRemovePrefix", $"/{pathAlias}" } }
                }
            };
            routes.Add(pathRoute);
            _logger.LogInformation("    Added Path-based Route: {Path}", pathRoute.Match.Path);
        }

        return new CustomProxyConfig(routes, clusters);
    }

    private class CustomProxyConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
        : IProxyConfig
    {
        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public Microsoft.Extensions.Primitives.IChangeToken ChangeToken { get; } = new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);
    }

    private class DirectRouteConfig
    {
        public string Path { get; set; }
        public string Service { get; set; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _watchCts.Cancel();
        _watchCts.Dispose();
    }
}