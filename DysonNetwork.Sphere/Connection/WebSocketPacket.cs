using System.Text.Json;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

public class WebSocketPacketType
{
    public const string Error = "error";
    public const string MessageNew = "messages.new";
    public const string MessageUpdate = "messages.update";
    public const string MessageDelete = "messages.delete";
    public const string CallParticipantsUpdate = "call.participants.update";
}

public class WebSocketPacket
{
    public string Type { get; set; } = null!;
    public object Data { get; set; } = null!;
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Creates a WebSocketPacket from raw WebSocket message bytes
    /// </summary>
    /// <param name="bytes">Raw WebSocket message bytes</param>
    /// <returns>Deserialized WebSocketPacket</returns>
    public static WebSocketPacket FromBytes(byte[] bytes)
    {
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        return JsonSerializer.Deserialize<WebSocketPacket>(json, jsonOpts) ??
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

        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        };
        return JsonSerializer.Deserialize<T>(
            JsonSerializer.Serialize(Data, jsonOpts),
            jsonOpts
        );
    }

    /// <summary>
    /// Serializes this WebSocketPacket to a byte array for sending over WebSocket
    /// </summary>
    /// <returns>Byte array representation of the packet</returns>
    public byte[] ToBytes()
    {
        var jsonOpts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        var json = JsonSerializer.Serialize(this, jsonOpts);
        return System.Text.Encoding.UTF8.GetBytes(json);
    }
}