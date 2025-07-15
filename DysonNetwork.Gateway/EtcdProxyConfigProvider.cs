using System.Text;
using dotnet_etcd.interfaces;
using Yarp.ReverseProxy.Configuration;

namespace DysonNetwork.Gateway;

public class EtcdProxyConfigProvider : IProxyConfigProvider, IDisposable
{
    private readonly IEtcdClient _etcdClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EtcdProxyConfigProvider> _logger;
    private readonly CancellationTokenSource _watchCts = new();
    private CancellationTokenSource _cts = new();

    public EtcdProxyConfigProvider(IEtcdClient etcdClient, IConfiguration configuration, ILogger<EtcdProxyConfigProvider> logger)
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

        var clusters = new List<ClusterConfig>();
        var routes = new List<RouteConfig>();

        var domainMappings = _configuration.GetSection("DomainMappings").GetChildren()
            .ToDictionary(x => x.Key, x => x.Value);

        _logger.LogInformation("Indexing {ServiceCount} services from Etcd.", kvs.Count);

        foreach (var kv in kvs)
        {
            var serviceName = Encoding.UTF8.GetString(kv.Key.ToByteArray()).Replace("/services/", "");
            var serviceUrl = Encoding.UTF8.GetString(kv.Value.ToByteArray());

            _logger.LogInformation("  Service: {ServiceName}, URL: {ServiceUrl}", serviceName, serviceUrl);

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
                Match = new RouteMatch { Path = $"/{serviceName}/{{**catch-all}}" }
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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _watchCts.Cancel();
        _watchCts.Dispose();
    }
}