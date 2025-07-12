using dotnet_etcd.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class EtcdStartup
{
    public static IServiceCollection AddEtcdService(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddEtcdClient(options =>
        {
            options.ConnectionString = configuration.GetConnectionString("Etcd");
            options.UseInsecureChannel = configuration.GetValue<bool>("Etcd:Insecure");
        });
        services.AddSingleton<ServiceRegistry>();

        return services;
    }
}