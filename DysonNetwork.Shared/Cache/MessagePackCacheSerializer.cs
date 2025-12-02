using MessagePack;

namespace DysonNetwork.Shared.Cache;

public class MessagePackCacheSerializer(MessagePackSerializerOptions? options = null) : ICacheSerializer
{
    private readonly MessagePackSerializerOptions _options = options ?? MessagePackSerializerOptions.Standard
        .WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance)
        .WithCompression(MessagePackCompression.Lz4BlockArray);

    public string Serialize<T>(T value)
    {
        var bytes = MessagePackSerializer.Serialize(value!, _options);
        return Convert.ToBase64String(bytes);
    }

    public T? Deserialize<T>(string data)
    {
        var bytes = Convert.FromBase64String(data);
        return MessagePackSerializer.Deserialize<T>(bytes, _options);
    }
}