using System.Net;
using Grpc.Net.Client;
using System.Security.Cryptography.X509Certificates;
using Grpc.Core;
using dotnet_etcd.interfaces;

namespace DysonNetwork.Shared.Proto;

public static class GrpcClientHelper
{
    private static CallInvoker CreateCallInvoker(
        string url,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var handler = new HttpClientHandler();
        handler.ClientCertificates.Add(
            clientCertPassword is null
                ? X509Certificate2.CreateFromPemFile(clientCertPath, clientKeyPath)
                : X509Certificate2.CreateFromEncryptedPemFile(clientCertPath, clientCertPassword, clientKeyPath)
        );
        var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestVersion = HttpVersion.Version20;
        httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        return GrpcChannel.ForAddress(url, new GrpcChannelOptions { HttpClient = httpClient }).CreateCallInvoker();
    }

    private static async Task<string> GetServiceUrlFromEtcd(IEtcdClient etcdClient, string serviceName)
    {
        var response = await etcdClient.GetAsync($"/services/{serviceName}");
        if (response.Kvs.Count == 0)
        {
            throw new InvalidOperationException($"Service '{serviceName}' not found in Etcd.");
        }

        return response.Kvs[0].Value.ToStringUtf8();
    }

    public static async Task<AccountService.AccountServiceClient> CreateAccountServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "DysonNetwork.Pass");
        return new AccountService.AccountServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }

    public static async Task<AuthService.AuthServiceClient> CreateAuthServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "DysonNetwork.Pass");
        return new AuthService.AuthServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }

    public static async Task<PusherService.PusherServiceClient> CreatePusherServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "DysonNetwork.Pusher");
        return new PusherService.PusherServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }
    
    public static async Task<FileService.FileServiceClient> CreateFileServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "DysonNetwork.File");
        return new FileService.FileServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }
    
    public static async Task<FileReferenceService.FileReferenceServiceClient> CreateFileReferenceServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "DysonNetwork.FileReference");
        return new FileReferenceService.FileReferenceServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }
}