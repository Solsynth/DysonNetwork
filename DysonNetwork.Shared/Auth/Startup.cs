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
        services.AddGrpcClient<AuthService.AuthServiceClient>(o =>
        {
            o.Address = new Uri("https://pass");
        });

        services.AddGrpcClient<PermissionService.PermissionServiceClient>(o =>
        {
            o.Address = new Uri("https://pass");
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