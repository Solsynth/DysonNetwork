using System.Text.Json;

namespace DysonNetwork.Shared.Cache;

public class JsonCacheSerializer(JsonSerializerOptions? options = null) : ICacheSerializer
{
    private readonly JsonSerializerOptions _options = options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        // Customize as needed (NodaTime, camelCase, converters, etc.)
        WriteIndented = false
    };

    // Customize as needed (NodaTime, camelCase, converters, etc.)

    public string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, _options);

    public T? Deserialize<T>(string data)
        => JsonSerializer.Deserialize<T>(data, _options);
}