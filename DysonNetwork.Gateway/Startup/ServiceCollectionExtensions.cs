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
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                };
            });

        services.AddRegistryService(configuration, addForwarder: false);
        services.AddSingleton<IProxyConfigProvider, RegistryProxyConfigProvider>();

        return services;
    }
}
