using dotnet_etcd.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Registry;

public static class RegistryStartup
{
    public static IServiceCollection AddRegistryService(
        this IServiceCollection services,
        IConfiguration configuration,
        bool addForwarder = true
    )
    {
        services.AddEtcdClient(options =>
        {
            options.ConnectionString = configuration.GetConnectionString("Etcd");
            options.UseInsecureChannel = configuration.GetValue<bool>("Etcd:Insecure");
        });
        services.AddSingleton<ServiceRegistry>();
        services.AddHostedService<RegistryHostedService>();

        if (addForwarder)
            services.AddHttpForwarder();

        return services;
    }
}