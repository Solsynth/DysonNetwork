using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DysonNetwork.Sphere.Fitness;

public class FitnessService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _fitnessUrl;

    public FitnessService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _fitnessUrl = configuration["Service:FitnessUrl"] ?? "http://localhost:5001";
    }

    public async Task<bool> ValidateAndGetOwnershipAsync(string fitnessType, Guid fitnessId, Guid userId)
    {
        var endpoint = fitnessType.ToLowerInvariant() switch
        {
            "workout" => $"api/workouts/{fitnessId}",
            "metric" => $"api/metrics/{fitnessId}",
            "goal" => $"api/goals/{fitnessId}",
            _ => null
        };

        if (endpoint is null)
            return false;

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var response = await httpClient.GetAsync($"{_fitnessUrl}/{endpoint}");
            if (!response.IsSuccessStatusCode)
                return false;

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("account_id", out var accountIdElement))
                return false;

            var accountId = Guid.Parse(accountIdElement.GetString()!);
            if (accountId != userId)
                return false;

            if (!root.TryGetProperty("visibility", out var visibilityElement))
                return true;

            var visibility = visibilityElement.GetInt32();
            return visibility == 1;
        }
        catch
        {
            return false;
        }
    }
}
