using dotnet_etcd.interfaces;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class ServiceInjectionHelper
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
        services.AddSingleton<AccountClientHelper>();
        
        services.AddSingleton<ActionLogService.ActionLogServiceClient>(sp =>
        {
            var etcdClient = sp.GetRequiredService<IEtcdClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var clientCertPath = config["Service:ClientCert"]!;
            var clientKeyPath = config["Service:ClientKey"]!;
            var clientCertPassword = config["Service:CertPassword"];

            return GrpcClientHelper
                .CreateActionLogServiceClient(etcdClient, clientCertPath, clientKeyPath, clientCertPassword)
                .GetAwaiter()
                .GetResult();
        }); 
        
        return services;
    }
    
    public static IServiceCollection AddDriveService(this IServiceCollection services)
    {
        services.AddSingleton<FileService.FileServiceClient>(sp =>
        {
            var etcdClient = sp.GetRequiredService<IEtcdClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var clientCertPath = config["Service:ClientCert"]!;
            var clientKeyPath = config["Service:ClientKey"]!;
            var clientCertPassword = config["Service:CertPassword"];

            return GrpcClientHelper
                .CreateFileServiceClient(etcdClient, clientCertPath, clientKeyPath, clientCertPassword)
                .GetAwaiter()
                .GetResult();
        });       
        
        services.AddSingleton<FileReferenceService.FileReferenceServiceClient>(sp =>
        {
            var etcdClient = sp.GetRequiredService<IEtcdClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var clientCertPath = config["Service:ClientCert"]!;
            var clientKeyPath = config["Service:ClientKey"]!;
            var clientCertPassword = config["Service:CertPassword"];

            return GrpcClientHelper
                .CreateFileReferenceServiceClient(etcdClient, clientCertPath, clientKeyPath, clientCertPassword)
                .GetAwaiter()
                .GetResult();
        });
        
        return services;
    }
}