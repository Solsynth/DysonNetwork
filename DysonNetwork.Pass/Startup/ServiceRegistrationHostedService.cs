using DysonNetwork.Shared.Registry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace DysonNetwork.Pass.Startup;

public class ServiceRegistrationHostedService : IHostedService
{
    private readonly ServiceRegistry _serviceRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ServiceRegistrationHostedService> _logger;

    public ServiceRegistrationHostedService(
        ServiceRegistry serviceRegistry,
        IConfiguration configuration,
        ILogger<ServiceRegistrationHostedService> logger)
    {
        _serviceRegistry = serviceRegistry;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var serviceName = "DysonNetwork.Pass"; // Preset service name
        var serviceUrl = _configuration["Service:Url"];

        if (string.IsNullOrEmpty(serviceName) || string.IsNullOrEmpty(serviceUrl))
        {
            _logger.LogWarning("Service name or URL not configured. Skipping Etcd registration.");
            return;
        }

        _logger.LogInformation("Registering service {ServiceName} at {ServiceUrl} with Etcd.", serviceName, serviceUrl);
        try
        {
            await _serviceRegistry.RegisterService(serviceName, serviceUrl);
            _logger.LogInformation("Service {ServiceName} registered successfully.", serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service {ServiceName} with Etcd.", serviceName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // The lease will expire automatically if the service stops.
        // For explicit unregistration, you would implement it here.
        _logger.LogInformation("Service registration hosted service is stopping.");
        return Task.CompletedTask;
    }
}
