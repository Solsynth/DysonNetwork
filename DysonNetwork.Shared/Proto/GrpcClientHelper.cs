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
            clientCertPassword is null ?
            X509Certificate2.CreateFromPemFile(clientCertPath, clientKeyPath) :
            X509Certificate2.CreateFromEncryptedPemFile(clientCertPath, clientCertPassword, clientKeyPath)
        );
        return GrpcChannel.ForAddress(url, new GrpcChannelOptions { HttpHandler = handler }).CreateCallInvoker();
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

    public static AccountService.AccountServiceClient CreateAccountServiceClient(
        string url,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        return new AccountService.AccountServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }

    public static async Task<AccountService.AccountServiceClient> CreateAccountServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "AccountService");
        return new AccountService.AccountServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }

    public static AuthService.AuthServiceClient CreateAuthServiceClient(
        string url,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        return new AuthService.AuthServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }

    public static async Task<AuthService.AuthServiceClient> CreateAuthServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "AuthService");
        return new AuthService.AuthServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }

    public static PusherService.PusherServiceClient CreatePusherServiceClient(
        string url,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        return new PusherService.PusherServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }

    public static async Task<PusherService.PusherServiceClient> CreatePusherServiceClient(
        IEtcdClient etcdClient,
        string clientCertPath,
        string clientKeyPath,
        string? clientCertPassword = null
    )
    {
        var url = await GetServiceUrlFromEtcd(etcdClient, "PusherService");
        return new PusherService.PusherServiceClient(CreateCallInvoker(url, clientCertPath, clientKeyPath,
            clientCertPassword));
    }
}