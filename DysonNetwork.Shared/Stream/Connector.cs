using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NATS.Client.Core;

namespace DysonNetwork.Shared.Stream;

public static class Connector
{
    public static IServiceCollection AddStreamConnection(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Stream");
        if (connectionString is null)
            throw new ArgumentNullException(nameof(connectionString));
        services.AddSingleton<INatsConnection>(_ => new NatsConnection(new NatsOpts()
        {
            Url = connectionString
        }));
        
        return services;
    }
}