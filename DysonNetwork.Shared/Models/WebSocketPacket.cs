using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Models;

public class WebSocketPacket
{
    public string Type { get; set; } = null!;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; } = null!;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Endpoint { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a WebSocketPacket from raw WebSocket message bytes
    /// </summary>
    /// <param name="bytes">Raw WebSocket message bytes</param>
    /// <returns>Deserialized WebSocketPacket</returns>
    public static WebSocketPacket FromBytes(byte[] bytes)
    {
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        return JsonSerializer.Deserialize<WebSocketPacket>(json, InfraObjectCoder.SerializerOptions) ??
               throw new JsonException("Failed to deserialize WebSocketPacket");
    }

    /// <summary>
    /// Deserializes the Data property to the specified type T
    /// </summary>
    /// <typeparam name="T">Target type to deserialize to</typeparam>
    /// <returns>Deserialized data of type T</returns>
    public T? GetData<T>()
    {
        if (Data is T typedData)
            return typedData;

        return JsonSerializer.Deserialize<T>(
            JsonSerializer.Serialize(Data, InfraObjectCoder.SerializerOptions),
            InfraObjectCoder.SerializerOptions
        );
    }

    /// <summary>
    /// Serializes this WebSocketPacket to a byte array for sending over WebSocket
    /// </summary>
    /// <returns>Byte array representation of the packet</returns>
    public byte[] ToBytes()
    {
        var json = JsonSerializer.Serialize(this, InfraObjectCoder.SerializerOptions);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }
    
    public Proto.WebSocketPacket ToProtoValue()
    {
        return new Proto.WebSocketPacket
        {
            Type = Type,
            Data = InfraObjectCoder.ConvertObjectToByteString(Data),
            ErrorMessage = ErrorMessage
        };
    }

    public static WebSocketPacket FromProtoValue(Proto.WebSocketPacket packet)
    {
        return new WebSocketPacket
        {
            Type = packet.Type,
            Data = InfraObjectCoder.ConvertByteStringToObject<object?>(packet.Data),
            ErrorMessage = packet.ErrorMessage
        };
    }
}