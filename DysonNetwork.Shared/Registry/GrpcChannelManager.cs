using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DysonNetwork.Shared.Registry;

public class GrpcChannelManager : IDisposable
{
    private readonly ConcurrentDictionary<string, GrpcChannel> _channels = new();
    private readonly ILogger<GrpcChannelManager> _logger;

    public GrpcChannelManager(ILogger<GrpcChannelManager> logger)
    {
        _logger = logger;
    }

    public GrpcChannel GetOrCreateChannel(string endpoint, string serviceName)
    {
        return _channels.GetOrAdd(endpoint, ep =>
        {
            _logger.LogInformation("Creating gRPC channel for {Service} at {Endpoint}", serviceName, ep);
            var options = new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 100 * 1024 * 1024, // 100MB
                MaxSendMessageSize = 100 * 1024 * 1024, // 100MB
            };
            return GrpcChannel.ForAddress(ep, options);
        });
    }

    public void Dispose()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Dispose();
        }

        _channels.Clear();
    }
}

public static class GrpcSharedChannelExtensions
{
    public static IServiceCollection AddSharedGrpcChannels(this IServiceCollection services)
    {
        services.AddSingleton<GrpcChannelManager>();
        return services;
    }

    public static IHttpClientBuilder ConfigureGrpcDefaults(this IHttpClientBuilder builder)
    {
        builder.ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
            MaxConnectionsPerServer = 2,
        });
        return builder;
    }

    public static IServiceCollection AddGrpcClientWithSharedChannel<TClient>(
        this IServiceCollection services,
        string endpoint,
        string serviceName
    ) where TClient : class
    {
        services.AddGrpcClient<TClient>(options => { options.Address = new Uri(endpoint); })
            .ConfigureGrpcDefaults();

        return services;
    }
}