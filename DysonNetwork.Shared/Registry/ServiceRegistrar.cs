using dotnet_etcd;
using Etcdserverpb;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DysonNetwork.Shared.Registry;

public class ServiceRegistrar(EtcdClient etcd)
{
    private CancellationTokenSource? _cts;
    private string? _serviceKey;
    private long _leaseId;
    private long _ttlSeconds;
    private readonly Dictionary<string, int> _roundRobinCounters = new();

    /// <summary>
    /// Register the service in etcd with a TTL lease.
    /// </summary>
    public async Task RegisterAsync(
        string serviceName,
        string servicePart,
        string instanceId,
        string host,
        int port,
        long ttlSeconds = 30)
    {
        _ttlSeconds = ttlSeconds;
        _serviceKey = $"/services/{serviceName}/{servicePart}/{instanceId}";
        var serviceValue = $"{host}:{port}";

        // Create and store TTL lease
        var leaseResp = await etcd.LeaseGrantAsync(new LeaseGrantRequest { TTL = _ttlSeconds });
        _leaseId = leaseResp.ID;

        await etcd.PutAsync(new PutRequest()
        {
            Key = ByteString.CopyFromUtf8(_serviceKey),
            Value = ByteString.CopyFromUtf8(serviceValue),
            Lease = _leaseId
        });

        // Start a background task to keep the lease alive
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    await etcd.LeaseKeepAlive(_leaseId, _cts.Token);
                    await Task.Delay(TimeSpan.FromSeconds(_ttlSeconds / 2), _cts.Token);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error keeping lease alive: {ex.Message}");
                    break;
                }
            }
        }, _cts.Token);
    }


    public async Task DeregisterAsync()
    {
        await _cts?.CancelAsync();

        if (_serviceKey != null)
        {
            try
            {
                await etcd.DeleteAsync(_serviceKey);
            }
            catch
            {
                // ignore delete errors
            }
        }
    }

    /// <summary>
    /// Get all service instances for a specific service name and part.
    /// </summary>
    public async Task<List<string>> GetServiceInstancesAsync(string serviceName, string servicePart)
    {
        var prefix = $"/services/{serviceName}/{servicePart}/";
        var request = new RangeRequest
        {
            Key = ByteString.CopyFromUtf8(prefix),
            RangeEnd = ByteString.CopyFromUtf8(prefix + "\0")
        };
        var response = await etcd.GetAsync(request);
        var instances = response.Kvs.Select(kv => kv.Value.ToStringUtf8()).ToList();
        return instances;
    }

    /// <summary>
    /// Get a single service instance with load balancing (round-robin).
    /// </summary>
    public async Task<string> GetServiceInstanceAsync(string serviceName, string servicePart)
    {
        var instances = await GetServiceInstancesAsync(serviceName, servicePart);
        if (instances.Count == 0)
            throw new InvalidOperationException($"No instances found for service '{serviceName}' part '{servicePart}'");
        var key = $"{serviceName}/{servicePart}";
        if (!_roundRobinCounters.ContainsKey(key))
            _roundRobinCounters[key] = 0;
        var instance = instances[_roundRobinCounters[key] % instances.Count];
        _roundRobinCounters[key] = (_roundRobinCounters[key] + 1) % int.MaxValue;
        return instance;
    }
}

public sealed class ServiceRegistrationOptions
{
    public string Name { get; set; } = null!;
    public string Host { get; set; } = "127.0.0.1";
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
}

public class ServiceRegistrarHostedService(
    ServiceRegistrar registrar,
    IConfiguration configuration,
    IOptions<ServiceRegistrationOptions> options
)
    : IHostedService
{
    private readonly ServiceRegistrationOptions _opts = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var grpcPort = int.Parse(configuration.GetValue("GRPC_PORT", "5000"));
        await registrar.RegisterAsync(_opts.Name, "grpc", _opts.InstanceId, _opts.Host, grpcPort);

        var httpPorts = configuration.GetValue("HTTP_PORTS", "6000")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.Parse(p.Trim()))
            .ToArray();
        await registrar.RegisterAsync(_opts.Name, "http", _opts.InstanceId, _opts.Host, httpPorts.First());
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await registrar.DeregisterAsync();
    }
}