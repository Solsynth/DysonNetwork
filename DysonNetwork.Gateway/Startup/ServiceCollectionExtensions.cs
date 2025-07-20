using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using DysonNetwork.Shared.Registry;
using Yarp.ReverseProxy.Configuration;

namespace DysonNetwork.Gateway.Startup;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGateway(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRequestTimeouts();

        services
            .AddReverseProxy()
            .ConfigureHttpClient((context, handler) =>
            {
                var caCert = X509CertificateLoader.LoadCertificateFromFile(configuration["CaCert"]!);
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    // TODO: check the ca in the future, for now just trust it, i need sleep
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                };
            });

        services.AddRegistryService(configuration);
        services.AddSingleton<IProxyConfigProvider, RegistryProxyConfigProvider>();

        return services;
    }
}