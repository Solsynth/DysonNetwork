using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Registry;

public class RegistryHostedService(
    ServiceRegistry serviceRegistry,
    IConfiguration configuration,
    ILogger<RegistryHostedService> logger
)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var serviceName = configuration["Service:Name"];
        var serviceUrl = configuration["Service:Url"];

        if (string.IsNullOrEmpty(serviceUrl) || string.IsNullOrEmpty(serviceName))
        {
            logger.LogWarning("Service URL or Service Name was not configured. Skipping Etcd registration.");
            return;
        }

        logger.LogInformation("Registering service {ServiceName} at {ServiceUrl} with Etcd.", serviceName, serviceUrl);
        try
        {
            await serviceRegistry.RegisterService(serviceName, serviceUrl);
            logger.LogInformation("Service {ServiceName} registered successfully.", serviceName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register service {ServiceName} with Etcd.", serviceName);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // The lease will expire automatically if the service stops ungracefully.
        var serviceName = configuration["Service:Name"];
        if (serviceName is not null)
            await serviceRegistry.UnregisterService(serviceName);
        logger.LogInformation("Service registration hosted service is stopping.");
    }
}