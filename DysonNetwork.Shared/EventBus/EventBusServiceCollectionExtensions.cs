using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DysonNetwork.Shared.EventBus;

public static class EventBusServiceCollectionExtensions
{
    public static IEventBusBuilder AddEventBus(this IServiceCollection services)
    {
        // Get the entry assembly name to uniquely identify this service
        var serviceName = Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
        
        services.AddSingleton<IEventBus, EventBus>();
        services.AddHostedService<EventBusStreamInitializer>();
        services.AddHostedService<EventBusBackgroundService>();
        
        return new EventBusBuilder(services, serviceName);
    }
    
    /// <summary>
    /// Add EventBus with an explicit service name for consumer identification.
    /// Use this if the automatic detection doesn't work correctly.
    /// </summary>
    public static IEventBusBuilder AddEventBus(this IServiceCollection services, string serviceName)
    {
        services.AddSingleton<IEventBus, EventBus>();
        services.AddHostedService<EventBusStreamInitializer>();
        services.AddHostedService<EventBusBackgroundService>();
        
        return new EventBusBuilder(services, serviceName);
    }
}
