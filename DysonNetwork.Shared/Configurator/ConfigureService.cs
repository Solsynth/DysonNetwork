using DysonNetwork.Shared.Registry;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;

namespace DysonNetwork.Shared.Configurator;

public class ConfigureService(ServiceRegistrar registrar)
{
    private readonly ServiceRegistrar _registrar = registrar;

    public async Task<JsonDocument> GetConfigurationsAsync()
    {
        var instance = await _registrar.GetServiceInstanceAsync("config", "http");
        using var client = new HttpClient();
        var response = await client.GetStringAsync($"http://{instance}/config");
        var json = JsonDocument.Parse(response);
        return json;
    }

    public async Task ConfigureAppAsync(IConfigurationBuilder builder)
    {
        var configs = await GetConfigurationsAsync();
        if (configs.RootElement.TryGetProperty("connection_strings", out var csElement))
        {
            var csJson = csElement.ToString();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(csJson));
            builder.AddJsonStream(stream);
        }
    }
}
