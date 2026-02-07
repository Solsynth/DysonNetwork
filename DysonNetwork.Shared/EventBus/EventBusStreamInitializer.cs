using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Net;

namespace DysonNetwork.Shared.EventBus;

public class EventBusStreamInitializer(
    IServiceProvider serviceProvider,
    ILogger<EventBusStreamInitializer> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var nats = scope.ServiceProvider.GetRequiredService<INatsConnection>();
        var subscriptions = scope.ServiceProvider.GetRequiredService<List<EventSubscription>>();
        
        if (subscriptions.Count == 0)
        {
            logger.LogInformation("No event subscriptions registered, skipping stream initialization");
            return;
        }

        var js = nats.CreateJetStreamContext();
        
        // Group subscriptions by stream name
        var streamGroups = subscriptions
            .Where(s => s.UseJetStream && !string.IsNullOrEmpty(s.StreamName))
            .GroupBy(s => s.StreamName!)
            .ToList();

        foreach (var group in streamGroups)
        {
            var streamName = group.Key;
            var subjects = group.Select(s => s.Subject).Distinct().ToList();
            
            try
            {
                await js.EnsureStreamCreated(streamName, subjects);
                logger.LogInformation("Ensured stream {StreamName} exists with subjects: {Subjects}", 
                    streamName, string.Join(", ", subjects));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to create stream {StreamName}", streamName);
                throw;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
