using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DysonNetwork.Shared.Proto;

public abstract class GrpcTypeHelper
{
    public static readonly JsonSerializerSettings SerializerSettings = new()
    {
        ContractResolver =  new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
        PreserveReferencesHandling = PreserveReferencesHandling.None,
        NullValueHandling = NullValueHandling.Include,
        DateParseHandling = DateParseHandling.None
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
                        result[kvp.Key] = JsonConvert.DeserializeObject(value.StringValue, SerializerSettings);
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
                    result[kvp.Key] = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value.StructValue.Fields.ToDictionary(f => f.Key, f => ConvertValueToObject(f.Value)), SerializerSettings));
                    break;
                case Value.KindOneofCase.ListValue:
                    result[kvp.Key] = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value.ListValue.Values.Select(ConvertValueToObject).ToList(), SerializerSettings), SerializerSettings);
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
            _ => JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value, SerializerSettings))
        };
    }
    
    public static T? ConvertValueToClass<T>(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => JsonConvert.DeserializeObject<T>(value.StringValue, SerializerSettings),
            _ => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value, SerializerSettings))
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
            _ => Value.ForString(JsonConvert.SerializeObject(obj, SerializerSettings)) // fallback to JSON string
        };
    }
    
    public static Value ConvertClassToValue<T>(T obj)
    {
        if (obj is JsonElement element)
            return Value.ForString(element.GetRawText());
        return obj switch
        {
            null => Value.ForNull(),
            _ => Value.ForString(JsonConvert.SerializeObject(obj, SerializerSettings))
        };
    }
}