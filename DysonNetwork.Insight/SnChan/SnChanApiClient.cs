using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Insight.SnChan;

/// <summary>
/// API client for SnChan bot operations
/// Uses separate bot account authentication
/// </summary>
public class SnChanApiClient
{
    private readonly HttpClient _httpClient;
    private readonly SnChanConfig _config;
    private readonly ILogger<SnChanApiClient> _logger;

    public SnChanApiClient(SnChanConfig config, ILogger<SnChanApiClient> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.GatewayUrl)
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("AtField", config.AccessToken);
    }

    private static string BuildUrl(string serviceName, string path, Dictionary<string, string>? queryParams = null)
    {
        // Normalize service name (remove leading/trailing slashes)
        var normalizedService = serviceName.Trim('/');

        // Normalize path: remove leading slash and ensure no double slashes
        var normalizedPath = path.TrimStart('/').Replace("//", "/");

        // Build the URL ensuring single slashes only
        var url = $"/{normalizedService}/{normalizedPath}";

        // Add query parameters if provided
        if (queryParams != null && queryParams.Count > 0)
        {
            var queryString = string.Join("&", queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
            url = $"{url}?{queryString}";
        }

        return url;
    }

    public async Task<T?> GetAsync<T>(string serviceName, string path)
    {
        var url = BuildUrl(serviceName, path);
        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, InfraObjectCoder.SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GET {Url}", url);
            throw;
        }
    }

    public async Task<T?> GetRawAsync<T>(string path)
    {
        try
        {
            var response = await _httpClient.GetAsync(path);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, InfraObjectCoder.SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GET {Path}", path);
            throw;
        }
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string serviceName,
        string path,
        TRequest data,
        Dictionary<string, string>? queryParams = null)
    {
        var url = BuildUrl(serviceName, path, queryParams);
        try
        {
            var json = JsonSerializer.Serialize(data, InfraObjectCoder.SerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TResponse>(responseContent, InfraObjectCoder.SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling POST {Url}", url);
            throw;
        }
    }

    public async Task<TResponse?> PostAsync<TResponse>(
        string serviceName,
        string path,
        object data,
        Dictionary<string, string>? queryParams = null)
    {
        return await PostAsync<object, TResponse>(serviceName, path, data, queryParams);
    }

    public async Task PostAsync(string serviceName, string path, object data, Dictionary<string, string>? queryParams = null)
    {
        var url = BuildUrl(serviceName, path, queryParams);
        try
        {
            var json = JsonSerializer.Serialize(data, InfraObjectCoder.SerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling POST {Url}", url);
            throw;
        }
    }

    public async Task DeleteAsync(string serviceName, string path)
    {
        var url = BuildUrl(serviceName, path);
        try
        {
            var response = await _httpClient.DeleteAsync(url);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling DELETE {Url}", url);
            throw;
        }
    }
}
