using DysonNetwork.Shared.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Auth;

public static class DysonAuthStartup
{
    public static IServiceCollection AddDysonAuth(
        this IServiceCollection services
    )
    {
        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AuthConstants.SchemeName;
                options.DefaultChallengeScheme = AuthConstants.SchemeName;
            })
            .AddScheme<DysonTokenAuthOptions, DysonTokenAuthHandler>(AuthConstants.SchemeName, _ => { });

        return services;
    }
}