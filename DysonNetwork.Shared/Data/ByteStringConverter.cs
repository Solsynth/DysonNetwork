using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Google.Protobuf;

namespace DysonNetwork.Shared.Data;

public class ByteStringConverter : JsonConverter<ByteString>
{
    public override ByteString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return ByteString.Empty;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return ByteString.FromBase64(reader.GetString());
        }
        
        //This is a temporary workaround to fix the issue that the old data in the cache is not in base64 format.
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var value = string.Empty;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return ByteString.CopyFromUtf8(value);
                }

                if (reader.TokenType == JsonTokenType.PropertyName && reader.GetString() == "$values")
                {
                    if (reader.Read() && reader.TokenType == JsonTokenType.String)
                    {
                        value = reader.GetString();
                    }
                }
            }
        }

        throw new JsonException("Expected a string or an object with a '$values' property.");
    }

    public override void Write(Utf8JsonWriter writer, ByteString value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToBase64());
    }
}
