using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using DysonNetwork.Shared.Data;
using Google.Protobuf;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace DysonNetwork.Shared.Proto;

public abstract class GrpcTypeHelper
{
    public static readonly JsonSerializerOptions? SerializerOptions = new JsonSerializerOptions()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { JsonExtensions.UnignoreAllProperties() }
        }
    }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    public static MapField<string, Value> ConvertToValueMap(Dictionary<string, object> source)
    {
        var result = new MapField<string, Value>();
        foreach (var kvp in source)
        {
            result[kvp.Key] = kvp.Value switch
            {
                string s => Value.ForString(s),
                int i => Value.ForNumber(i),
                long l => Value.ForNumber(l),
                float f => Value.ForNumber(f),
                double d => Value.ForNumber(d),
                bool b => Value.ForBool(b),
                null => Value.ForNull(),
                _ => Value.ForString(JsonSerializer.Serialize(kvp.Value, SerializerOptions)) // fallback to JSON string
            };
        }
        return result;
    }

    public static Dictionary<string, object?> ConvertFromValueMap(MapField<string, Value> source)
    {
        var result = new Dictionary<string, object?>();
        foreach (var kvp in source)
        {
            var value = kvp.Value;
            switch (value.KindCase)
            {
                case Value.KindOneofCase.StringValue:
                    try
                    {
                        // Try to parse as JSON object or primitive
                        result[kvp.Key] = JsonNode.Parse(value.StringValue)?.AsObject() ?? JsonObject.Create(new JsonElement());
                    }
                    catch
                    {
                        // Fallback to raw string
                        result[kvp.Key] = value.StringValue;
                    }
                    break;
                case Value.KindOneofCase.NumberValue:
                    result[kvp.Key] = value.NumberValue;
                    break;
                case Value.KindOneofCase.BoolValue:
                    result[kvp.Key] = value.BoolValue;
                    break;
                case Value.KindOneofCase.NullValue:
                    result[kvp.Key] = null;
                    break;
                case Value.KindOneofCase.StructValue:
                    result[kvp.Key] = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(
                            value.StructValue.Fields.ToDictionary(f => f.Key, f => ConvertValueToObject(f.Value)),
                            SerializerOptions
                        ),
                        SerializerOptions
                    );
                    break;
                case Value.KindOneofCase.ListValue:
                    result[kvp.Key] = JsonSerializer.Deserialize<JsonElement>(
                        JsonSerializer.Serialize(
                            value.ListValue.Values.Select(ConvertValueToObject).ToList(),
                            SerializerOptions
                        ),
                        SerializerOptions
                    );
                    break;
                default:
                    result[kvp.Key] = null;
                    break;
            }
        }
        return result;
    }

    public static object? ConvertValueToObject(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.NullValue => null,
            _ => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(value, SerializerOptions), SerializerOptions)
        };
    }

    public static T? ConvertValueToClass<T>(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => JsonSerializer.Deserialize<T>(value.StringValue, SerializerOptions),
            _ => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, SerializerOptions), SerializerOptions)
        };
    }

    public static Value ConvertObjectToValue(object? obj)
    {
        return obj switch
        {
            string s => Value.ForString(s),
            int i => Value.ForNumber(i),
            long l => Value.ForNumber(l),
            float f => Value.ForNumber(f),
            double d => Value.ForNumber(d),
            bool b => Value.ForBool(b),
            null => Value.ForNull(),
            _ => Value.ForString(JsonSerializer.Serialize(obj, SerializerOptions)) // fallback to JSON string
        };
    }

    public static Value ConvertClassToValue<T>(T obj)
    {
        if (obj is JsonElement element)
            return Value.ForString(element.GetRawText());
        return obj switch
        {
            null => Value.ForNull(),
            _ => Value.ForString(JsonSerializer.Serialize(obj, SerializerOptions))
        };
    }

    public static ByteString ConvertObjectToByteString(object? obj)
    {
        return ByteString.CopyFromUtf8(
            JsonSerializer.Serialize(obj, SerializerOptions)
        );
    }
    
    public static T? ConvertByteStringToObject<T>(ByteString bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes.ToStringUtf8(), SerializerOptions);
    }
}
