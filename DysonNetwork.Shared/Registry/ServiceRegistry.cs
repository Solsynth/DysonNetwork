using System.Text;
using dotnet_etcd.interfaces;
using Etcdserverpb;
using Google.Protobuf;

namespace DysonNetwork.Shared.Registry;

public class ServiceRegistry(IEtcdClient etcd)
{
    public async Task RegisterService(string serviceName, string serviceUrl, long leaseTtlSeconds = 60)
    {
        var key = $"/services/{serviceName}";
        var leaseResponse = await etcd.LeaseGrantAsync(new LeaseGrantRequest { TTL = leaseTtlSeconds });
        await etcd.PutAsync(new PutRequest
        {
            Key = ByteString.CopyFrom(key, Encoding.UTF8),
            Value = ByteString.CopyFrom(serviceUrl, Encoding.UTF8),
            Lease = leaseResponse.ID
        });
        await etcd.LeaseKeepAlive(leaseResponse.ID, CancellationToken.None);
    }

    public async Task UnregisterService(string serviceName)
    {
        var key = $"/services/{serviceName}";
        await etcd.DeleteAsync(key);
    }
}