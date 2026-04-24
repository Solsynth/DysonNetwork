using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using DysonNetwork.Insight.Agent.Foundation;

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

    private static readonly Dictionary<string, JsonObject> _swaggerCache = new();

    [AgentTool("list_services", Description = "List all available services that have Swagger API documentation. Returns JSON array of service names.")]
    public async Task<string> ListServices()
    {
        logger.LogDebug("Listing available services");
        return JsonSerializer.Serialize(new { services = AvailableServices }, JsonOptions);
    }

    [AgentTool("get_swagger_docs", Description = "Get summarized Swagger API documentation for a service. Returns a compact summary with service info and list of operations.")]
    public async Task<string> GetSwaggerDocs(
        [AgentToolParameter("The service name to get Swagger docs for (e.g., 'sphere', 'passport', 'messager', 'drive', 'wallet', 'ring', 'padlock', 'fitness', 'zone')")]
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
            
            if (_swaggerCache.TryGetValue(normalizedService, out var cachedSwagger))
            {
                return BuildSummaryResponse(normalizedService, cachedSwagger);
            }

            var swaggerUrl = $"/swagger/{normalizedService}/v1/swagger.json";
            var rawJson = await apiClient.GetRawAsync<string>(swaggerUrl);

            if (string.IsNullOrEmpty(rawJson))
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = false, 
                    error = $"No Swagger docs found for service: {normalizedService}" 
                }, JsonOptions);
            }

            var swaggerObj = JsonSerializer.Deserialize<JsonObject>(rawJson);
            if (swaggerObj == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = false, 
                    error = $"Failed to parse Swagger docs for: {normalizedService}" 
                }, JsonOptions);
            }

            _swaggerCache[normalizedService] = swaggerObj;
            return BuildSummaryResponse(normalizedService, swaggerObj);
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

    [AgentTool("get_swagger_operation", Description = "Get detailed API specification for a specific operation. Parameters: service name, HTTP path, and HTTP method.")]
    public async Task<string> GetSwaggerOperation(
        [AgentToolParameter("The service name (e.g., 'sphere', 'passport', 'ring')")]
        string serviceName,
        [AgentToolParameter("The API path (e.g., '/posts', '/notifications/{id}')")]
        string path,
        [AgentToolParameter("The HTTP method (e.g., 'get', 'post', 'put', 'delete')")]
        string method = "get"
    )
    {
        var normalizedService = serviceName.ToLowerInvariant().Trim();
        var normalizedMethod = method.ToLowerInvariant().Trim();
        var normalizedPath = path.Trim();

        try
        {
            JsonObject? swaggerObj;
            if (!_swaggerCache.TryGetValue(normalizedService, out swaggerObj))
            {
                var swaggerUrl = $"/swagger/{normalizedService}/v1/swagger.json";
                var rawJson = await apiClient.GetRawAsync<string>(swaggerUrl);
                
                if (string.IsNullOrEmpty(rawJson))
                {
                    return JsonSerializer.Serialize(new 
                    { 
                        success = false, 
                        error = $"No Swagger docs found for service: {normalizedService}" 
                    }, JsonOptions);
                }

                swaggerObj = JsonSerializer.Deserialize<JsonObject>(rawJson);
                if (swaggerObj == null)
                {
                    return JsonSerializer.Serialize(new 
                    { 
                        success = false, 
                        error = $"Failed to parse Swagger docs for: {normalizedService}" 
                    }, JsonOptions);
                }

                _swaggerCache[normalizedService] = swaggerObj;
            }

            var paths = swaggerObj?["paths"] as JsonObject;
            if (paths == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = false, 
                    error = "No paths found in Swagger docs" 
                }, JsonOptions);
            }

            var pathKey = paths.FirstOrDefault(p => 
                p.Key.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                p.Key.Equals(normalizedPath.TrimStart('/'), StringComparison.OrdinalIgnoreCase) ||
                NormalizePath(p.Key) == NormalizePath(normalizedPath)
            ).Key;

            if (string.IsNullOrEmpty(pathKey))
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = false, 
                    error = $"Path not found: {path}" 
                }, JsonOptions);
            }

            var pathItem = paths[pathKey] as JsonObject;
            var methodItem = pathItem?[normalizedMethod] as JsonObject;

            if (methodItem == null)
            {
                return JsonSerializer.Serialize(new 
                { 
                    success = false, 
                    error = $"Method '{method}' not found for path: {path}" 
                }, JsonOptions);
            }

            var operationId = methodItem["operationId"]?.GetValue<string>();
            var summary = methodItem["summary"]?.GetValue<string>();
            var description = methodItem["description"]?.GetValue<string>();
            var tags = methodItem["tags"]?.AsArray().Select(t => t?.GetValue<string>()).ToList();
            
            var parameters = new List<object>();
            var paramsArray = methodItem["parameters"] as JsonArray;
            if (paramsArray != null)
            {
                foreach (var p in paramsArray)
                {
                    var paramObj = p as JsonObject;
                    if (paramObj != null)
                    {
                        parameters.Add(new
                        {
                            name = paramObj["name"]?.GetValue<string>(),
                            @in = paramObj["in"]?.GetValue<string>(),
                            required = paramObj["required"]?.GetValue<bool>() ?? false,
                            description = paramObj["description"]?.GetValue<string>(),
                            schema = paramObj["schema"]?["type"]?.GetValue<string>()
                        });
                    }
                }
            }

            var requestBody = methodItem["requestBody"] as JsonObject;
            object? requestBodySpec = null;
            if (requestBody != null)
            {
                var content = requestBody["content"] as JsonObject;
                var jsonContent = content?["application/json"] as JsonObject;
                var schema = jsonContent?["schema"] as JsonObject;
                requestBodySpec = new
                {
                    description = requestBody["description"]?.GetValue<string>(),
                    required = requestBody["required"]?.GetValue<bool>() ?? false,
                    schema = ExtractSchemaRef(schema)
                };
            }

            var responses = methodItem["responses"] as JsonObject;
            var responsesDict = new Dictionary<string, object>();
            if (responses != null)
            {
                foreach (var resp in responses)
                {
                    var respObj = resp.Value as JsonObject;
                    if (respObj != null)
                    {
                        responsesDict[resp.Key] = new
                        {
                            description = respObj["description"]?.GetValue<string>(),
                            schema = ExtractSchemaRef(respObj["content"]?["application/json"]?["schema"] as JsonObject)
                        };
                    }
                }
            }

            return JsonSerializer.Serialize(new 
            { 
                success = true,
                service = normalizedService,
                operation = new
                {
                    path = pathKey,
                    method = normalizedMethod,
                    operationId,
                    summary,
                    description,
                    tags,
                    parameters,
                    requestBody = requestBodySpec,
                    responses = responsesDict
                }
            }, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get operation details for {ServiceName} {Method} {Path}", 
                normalizedService, normalizedMethod, normalizedPath);
            return JsonSerializer.Serialize(new 
            { 
                success = false, 
                error = $"Failed to get operation details: {ex.Message}" 
            }, JsonOptions);
        }
    }

    private string BuildSummaryResponse(string serviceName, JsonObject swagger)
    {
        var info = swagger["info"] as JsonObject;
        var infoTitle = info?["title"]?.GetValue<string>() ?? serviceName;
        var infoVersion = info?["version"]?.GetValue<string>() ?? "1.0";

        var paths = swagger["paths"] as JsonObject;
        var operations = new List<object>();
        var allTags = new HashSet<string>();

        if (paths != null)
        {
            foreach (var path in paths)
            {
                var pathItem = path.Value as JsonObject;
                if (pathItem == null) continue;

                foreach (var method in pathItem)
                {
                    var methodItem = method.Value as JsonObject;
                    if (methodItem == null) continue;

                    var operationId = methodItem["operationId"]?.GetValue<string>();
                    var summary = methodItem["summary"]?.GetValue<string>();
                    var description = methodItem["description"]?.GetValue<string>();
                    var tags = methodItem["tags"]?.AsArray().Select(t => t?.GetValue<string>()).ToList();

                    if (tags != null)
                    {
                        foreach (var tag in tags)
                        {
                            if (!string.IsNullOrEmpty(tag)) allTags.Add(tag);
                        }
                    }

                    operations.Add(new
                    {
                        method = method.Key.ToUpperInvariant(),
                        path = path.Key,
                        operationId = operationId ?? "",
                        summary = summary ?? description ?? ""
                    });
                }
            }
        }

        return JsonSerializer.Serialize(new 
        { 
            success = true, 
            service = serviceName, 
            info = new { title = infoTitle, version = infoVersion },
            operations = operations,
            tags = allTags.ToList()
        }, JsonOptions);
    }

    private string? NormalizePath(string path)
    {
        return path.TrimStart('/').ToLowerInvariant();
    }

    private object? ExtractSchemaRef(JsonObject? schema)
    {
        if (schema == null) return null;

        var refValue = schema["$ref"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(refValue))
        {
            return new { @ref = refValue };
        }

        var type = schema["type"]?.GetValue<string>();
        var format = schema["format"]?.GetValue<string>();
        
        if (type == "array")
        {
            return new { type, items = ExtractSchemaRef(schema["items"] as JsonObject) };
        }
        
        if (type == "object" || schema.ContainsKey("properties"))
        {
            var props = new Dictionary<string, object?>();
            var propsObj = schema["properties"] as JsonObject;
            if (propsObj != null)
            {
                foreach (var p in propsObj)
                {
                    props[p.Key] = ExtractSchemaRef(p.Value as JsonObject);
                }
            }
            return new { type, properties = props };
        }

        return new { type, format };
    }
}
