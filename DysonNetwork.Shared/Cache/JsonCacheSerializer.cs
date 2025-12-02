using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using DysonNetwork.Shared.Data;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace DysonNetwork.Shared.Cache;

public class JsonCacheSerializer : ICacheSerializer
{
    private readonly JsonSerializerOptions _options;

    public JsonCacheSerializer()
    {
        _options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { JsonExtensions.UnignoreAllProperties() },
            },
            ReferenceHandler = ReferenceHandler.Preserve,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new ByteStringConverter() }
        };
        _options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        _options.PropertyNameCaseInsensitive = true;
    }

    public string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, _options);

    public T? Deserialize<T>(string data)
        => JsonSerializer.Deserialize<T>(data, _options);
}