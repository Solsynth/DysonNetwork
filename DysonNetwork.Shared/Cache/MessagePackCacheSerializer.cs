using MessagePack;
using MessagePack.NodaTime;
using MessagePack.Resolvers;

namespace DysonNetwork.Shared.Cache;

public class MessagePackCacheSerializer(MessagePackSerializerOptions? options = null) : ICacheSerializer
{
    private readonly MessagePackSerializerOptions _options = options ?? MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(
            BuiltinResolver.Instance,
            AttributeFormatterResolver.Instance,
            NodatimeResolver.Instance,
            DynamicEnumAsStringResolver.Instance,
            ContractlessStandardResolver.Instance
        ))
        .WithCompression(MessagePackCompression.Lz4BlockArray)
        .WithSecurity(MessagePackSecurity.UntrustedData)
        .WithOmitAssemblyVersion(true);

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