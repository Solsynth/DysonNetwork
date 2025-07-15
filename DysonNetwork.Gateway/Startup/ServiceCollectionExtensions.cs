using DysonNetwork.Shared.Registry;
using Yarp.ReverseProxy.Configuration;

namespace DysonNetwork.Gateway.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddReverseProxy();
        services.AddRegistryService(configuration);
        services.AddSingleton<IProxyConfigProvider, RegistryProxyConfigProvider>();

        return services;
    }
}
