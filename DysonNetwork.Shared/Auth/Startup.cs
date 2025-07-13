using dotnet_etcd.interfaces;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Auth;

public static class DysonAuthStartup
{
    public static IServiceCollection AddDysonAuth(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddSingleton(sp =>
        {
            var etcdClient = sp.GetRequiredService<IEtcdClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var clientCertPath = config["ClientCert:Path"];
            var clientKeyPath = config["ClientKey:Path"];
            var clientCertPassword = config["ClientCert:Password"];

            return GrpcClientHelper.CreateAuthServiceClient(etcdClient, clientCertPath, clientKeyPath, clientCertPassword);
        });
        
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthConstants.SchemeName;
                options.DefaultChallengeScheme = AuthConstants.SchemeName;
            })
            .AddScheme<DysonTokenAuthOptions, DysonTokenAuthHandler>(AuthConstants.SchemeName, _ => { });

        return services;
    }
}