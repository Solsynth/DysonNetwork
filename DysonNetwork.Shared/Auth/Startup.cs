using dotnet_etcd.interfaces;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Auth;

public static class DysonAuthStartup
{
    public static IServiceCollection AddDysonAuth(
        this IServiceCollection services
    )
    {
        services.AddSingleton<AuthService.AuthServiceClient>(sp =>
        {
            var etcdClient = sp.GetRequiredService<IEtcdClient>();
            var config = sp.GetRequiredService<IConfiguration>();
            var clientCertPath = config["Service:ClientCert"];
            var clientKeyPath = config["Service:ClientKey"];
            var clientCertPassword = config["Service:CertPassword"];

            return GrpcClientHelper
                .CreateAuthServiceClient(etcdClient, clientCertPath, clientKeyPath, clientCertPassword)
                .GetAwaiter()
                .GetResult();
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