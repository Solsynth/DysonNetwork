using NodaTime;

namespace DysonNetwork.Gateway.Health;

public class GatewayHealthAggregator(IHttpClientFactory httpClientFactory, GatewayReadinessStore store)
    : BackgroundService
{
    private async Task<ServiceHealthState> CheckService(string serviceName)
    {
        var client = httpClientFactory.CreateClient("health");
        var now = SystemClock.Instance.GetCurrentInstant();

        try
        {
            // Use the service discovery to lookup service
            // The service defaults give every single service a health endpoint that we can use here
            using var response = await client.GetAsync($"http://{serviceName}/health");

            if (response.IsSuccessStatusCode)
            {
                return new ServiceHealthState(
                    serviceName,
                    true,
                    now,
                    null
                );
            }

            return new ServiceHealthState(
                serviceName,
                false,
                now,
                $"StatusCode: {(int)response.StatusCode}"
            );
        }
        catch (Exception ex)
        {
            return new ServiceHealthState(
                serviceName,
                false,
                now,
                ex.Message
            );
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var service in GatewayConstant.ServiceNames)
            {
                var result = await CheckService(service);
                store.Update(result);
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
