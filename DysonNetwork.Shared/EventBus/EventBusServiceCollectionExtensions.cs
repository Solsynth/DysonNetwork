using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DysonNetwork.Shared.EventBus;

public static class EventBusServiceCollectionExtensions
{
    public static IEventBusBuilder AddEventBus(this IServiceCollection services)
    {
        services.AddSingleton<IEventBus, EventBus>();
        services.AddHostedService<EventBusBackgroundService>();
        
        return new EventBusBuilder(services);
    }
}
