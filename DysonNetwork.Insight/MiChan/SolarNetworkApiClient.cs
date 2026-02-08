using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DysonNetwork.Shared.Data;

namespace DysonNetwork.Insight.MiChan;

public class SolarNetworkApiClient
{
    private readonly HttpClient _httpClient;
    private readonly MiChanConfig _config;
    private readonly ILogger<SolarNetworkApiClient> _logger;

    public SolarNetworkApiClient(MiChanConfig config, ILogger<SolarNetworkApiClient> logger)
    {
        _config = config;
        _logger = logger;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.GatewayUrl)
        };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("AtField", config.AccessToken);
    }

    private static string BuildUrl(string serviceName, string path)
    {
        var normalizedPath = path.StartsWith("/") ? path[1..] : path;
        return $"/{serviceName}/{normalizedPath}";
    }

    public async Task<T?> GetAsync<T>(string serviceName, string path)
    {
        try
        {
            var url = BuildUrl(serviceName, path);
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, InfraObjectCoder.SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GET {Service}/{Path}", serviceName, path);
            throw;
        }
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string serviceName, string path, TRequest data)
    {
        try
        {
            var url = BuildUrl(serviceName, path);
            var json = JsonSerializer.Serialize(data, InfraObjectCoder.SerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TResponse>(responseContent, InfraObjectCoder.SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling POST {Service}/{Path}", serviceName, path);
            throw;
        }
    }

    public async Task<TResponse?> PostAsync<TResponse>(string serviceName, string path, object data)
    {
        return await PostAsync<object, TResponse>(serviceName, path, data);
    }

    public async Task PostAsync(string serviceName, string path, object data)
    {
        try
        {
            var url = BuildUrl(serviceName, path);
            var json = JsonSerializer.Serialize(data, InfraObjectCoder.SerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling POST {Service}/{Path}", serviceName, path);
            throw;
        }
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string serviceName, string path, TRequest data)
    {
        try
        {
            var url = BuildUrl(serviceName, path);
            var json = JsonSerializer.Serialize(data, InfraObjectCoder.SerializerOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PutAsync(url, content);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<TResponse>(responseContent, InfraObjectCoder.SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling PUT {Service}/{Path}", serviceName, path);
            throw;
        }
    }

    public async Task DeleteAsync(string serviceName, string path)
    {
        try
        {
            var url = BuildUrl(serviceName, path);
            var response = await _httpClient.DeleteAsync(url);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling DELETE {Service}/{Path}", serviceName, path);
            throw;
        }
    }
}
