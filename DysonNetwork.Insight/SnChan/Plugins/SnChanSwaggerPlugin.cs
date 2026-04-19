using System.ComponentModel;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace DysonNetwork.Insight.SnChan.Plugins;

public class SnChanSwaggerPlugin(
    SnChanApiClient apiClient,
    ILogger<SnChanSwaggerPlugin> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly List<string> AvailableServices =
    [
        "sphere",
        "passport",
        "messager",
        "drive",
        "wallet",
        "ring",
        "padlock",
        "fitness",
        "zone"
    ];

    [KernelFunction("list_services")]
    [Description("List all available services that have Swagger API documentation. Returns JSON array of service names.")]
    public async Task<string> ListServices()
    {
        logger.LogDebug("Listing available services");
        return JsonSerializer.Serialize(new { services = AvailableServices }, JsonOptions);
    }

    [KernelFunction("get_swagger_docs")]
    [Description("Get Swagger API documentation for a specific service. Returns the full Swagger JSON document.")]
    public async Task<string> GetSwaggerDocs(
        [Description("The service name to get Swagger docs for (e.g., 'sphere', 'passport', 'messager', 'drive', 'wallet', 'ring', 'padlock', 'fitness', 'zone')")]
        string serviceName
    )
    {
        var normalizedService = serviceName.ToLowerInvariant().Trim();

        if (!AvailableServices.Contains(normalizedService))
        {
            logger.LogWarning("Invalid service name: {ServiceName}", serviceName);
            return JsonSerializer.Serialize(new 
            { 
                success = false, 
                error = $"Unknown service: {serviceName}. Valid services are: {string.Join(", ", AvailableServices)}" 
            }, JsonOptions);
        }

        try
        {
            logger.LogDebug("Fetching swagger docs for service: {ServiceName}", normalizedService);
            
            var swaggerUrl = $"/swagger/{normalizedService}/v1/swagger.json";
            var result = await apiClient.GetAsync<object>("", swaggerUrl);

            if (result == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = false, 
                    error = $"No Swagger docs found for service: {normalizedService}" 
                }, JsonOptions);
            }

            return JsonSerializer.Serialize(new { success = true, service = normalizedService, docs = result }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get swagger docs for service: {ServiceName}", normalizedService);
            return JsonSerializer.Serialize(new 
            { 
                success = false, 
                error = $"Failed to fetch Swagger docs: {ex.Message}" 
            }, JsonOptions);
        }
    }
}