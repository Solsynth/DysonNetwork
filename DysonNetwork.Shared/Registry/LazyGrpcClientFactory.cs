using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Registry;

public interface IGrpcClientFactory<out TClient> where TClient : class
{
    TClient CreateClient();
}

public class LazyGrpcClientFactory<TClient>(
    IServiceProvider serviceProvider,
    ILogger<LazyGrpcClientFactory<TClient>> logger
) : IGrpcClientFactory<TClient> where TClient : class
{
    private TClient? _client;
    private readonly Lock _lock = new();

    public TClient CreateClient()
    {
        if (Volatile.Read(ref _client) != null)
        {
            return Volatile.Read(ref _client)!;
        }

        lock (_lock)
        {
            if (Volatile.Read(ref _client) != null)
            {
                return Volatile.Read(ref _client)!;
            }

            var client = serviceProvider.GetRequiredService<TClient>();
            Volatile.Write(ref _client, client);
            logger.LogInformation("Lazy initialized gRPC client: {ClientType}", typeof(TClient).Name);
            return Volatile.Read(ref _client)!;
        }
    }
}

public static class GrpcClientFactoryExtensions
{
    public static IServiceCollection AddLazyGrpcClientFactory<TClient>(this IServiceCollection services)
        where TClient : class
    {
        services.AddScoped<LazyGrpcClientFactory<TClient>>();
        services.AddScoped<IGrpcClientFactory<TClient>>(sp => sp.GetRequiredService<LazyGrpcClientFactory<TClient>>());
        return services;
    }
}
