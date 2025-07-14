using dotnet_etcd.interfaces;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class ServiceHelper
{
    public static IServiceCollection AddPusherService(this IServiceCollection services)
    {
        services.AddSingleton<PusherService.PusherServiceClient>(sp =>
        {
            var etcdClient = sp.GetRequiredService<IEtcdClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var clientCertPath = config["Service:ClientCert"]!;
            var clientKeyPath = config["Service:ClientKey"]!;
            var clientCertPassword = config["Service:CertPassword"];

            return GrpcClientHelper
                .CreatePusherServiceClient(etcdClient, clientCertPath, clientKeyPath, clientCertPassword)
                .GetAwaiter()
                .GetResult();
        });       
        
        return services;
    }
    
    public static IServiceCollection AddAccountService(this IServiceCollection services)
    {
        services.AddSingleton<AccountService.AccountServiceClient>(sp =>
        {
            var etcdClient = sp.GetRequiredService<IEtcdClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var clientCertPath = config["Service:ClientCert"]!;
            var clientKeyPath = config["Service:ClientKey"]!;
            var clientCertPassword = config["Service:CertPassword"];

            return GrpcClientHelper
                .CreateAccountServiceClient(etcdClient, clientCertPath, clientKeyPath, clientCertPassword)
                .GetAwaiter()
                .GetResult();
        });       
        
        return services;
    }
}