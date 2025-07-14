using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;

namespace DysonNetwork.Shared.Proto;

public abstract class GrpcTypeHelper
{
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        PreserveReferencesHandling = PreserveReferencesHandling.All,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

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
                _ => Value.ForString(JsonConvert.SerializeObject(kvp.Value, SerializerSettings)) // fallback to JSON string
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
                        result[kvp.Key] = JsonConvert.DeserializeObject(value.StringValue);
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
                    result[kvp.Key] = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value.StructValue.Fields.ToDictionary(f => f.Key, f => ConvertField(f.Value)), SerializerSettings));
                    break;
                case Value.KindOneofCase.ListValue:
                    result[kvp.Key] = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value.ListValue.Values.Select(ConvertField).ToList(), SerializerSettings));
                    break;
                default:
                    result[kvp.Key] = null;
                    break;
            }
        }
        return result;
    }

    public static object? ConvertField(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.NullValue => null,
            _ => JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value, SerializerSettings))
        };
    }
}