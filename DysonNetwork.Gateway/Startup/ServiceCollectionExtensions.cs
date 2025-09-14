using System.Net.Security;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

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
                // var caCert = X509CertificateLoader.LoadCertificateFromFile(configuration["CaCert"]!);
                handler.SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
                };
            })
            .AddTransforms(context =>
            {
                context.CopyRequestHeaders = true;
                context.AddOriginalHost();
                context.AddForwarded(action: ForwardedTransformActions.Set);
                context.AddXForwarded(action: ForwardedTransformActions.Set);
            });

        services.AddSingleton<IProxyConfigProvider, RegistryProxyConfigProvider>();

        return services;
    }
}
