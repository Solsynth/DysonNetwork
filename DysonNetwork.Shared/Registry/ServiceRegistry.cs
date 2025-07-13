using System.Text;
using dotnet_etcd.interfaces;
using Etcdserverpb;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Registry;

public class ServiceRegistry(IEtcdClient etcd, ILogger<ServiceRegistry> logger)
{
    public async Task RegisterService(string serviceName, string serviceUrl, long leaseTtlSeconds = 60, CancellationToken cancellationToken = default)
    {
        var key = $"/services/{serviceName}";
        var leaseResponse = await etcd.LeaseGrantAsync(new LeaseGrantRequest { TTL = leaseTtlSeconds });
        await etcd.PutAsync(new PutRequest
        {
            Key = ByteString.CopyFrom(key, Encoding.UTF8),
            Value = ByteString.CopyFrom(serviceUrl, Encoding.UTF8),
            Lease = leaseResponse.ID
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await etcd.LeaseKeepAlive(leaseResponse.ID, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError($"Lease keep-alive failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    public async Task UnregisterService(string serviceName)
    {
        var key = $"/services/{serviceName}";
        await etcd.DeleteAsync(key);
    }
}