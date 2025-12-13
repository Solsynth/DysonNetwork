using dotnet_etcd;
using Etcdserverpb;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DysonNetwork.Shared.Registry;

public class ServiceRegistrar(EtcdClient etcd)
{
    private CancellationTokenSource? _cts;
    private string? _serviceKey;
    private long _leaseId;
    private long _ttlSeconds;

    /// <summary>
    /// Register the service in etcd with a TTL lease.
    /// </summary>
    public async Task RegisterAsync(string serviceName, string servicePart, string instanceId, string host, int port, long ttlSeconds = 30)
    {
        _ttlSeconds = ttlSeconds;
        _serviceKey = $"/services/{serviceName}/${servicePart}/{instanceId}";
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
}

public class ServiceRegistrarHostedService(ServiceRegistrar registrar, IConfiguration config) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var name = config["Service:Name"];
        var host = config["Service:Host"];
        var grpcPort = int.Parse(config["Service:GrpcPort"]!);
        var httpPort = int.Parse(config["Service:HttpPort"]!);
        var instanceId = config["Service:InstanceId"] ?? Guid.NewGuid().ToString("N");

        await registrar.RegisterAsync(name, "grpc", instanceId, host, grpcPort);
        await registrar.RegisterAsync(name, "http", instanceId, host, httpPort);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await registrar.DeregisterAsync();
    }
}