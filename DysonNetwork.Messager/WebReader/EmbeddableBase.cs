using System.Text.Json;
using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Messager.WebReader;

/// <summary>
/// The embeddable can be used in the post or messages' meta's embeds fields
/// To render a richer type of content.
///
/// A simple example of using link preview embed:
/// <code>
/// {
///     // ... post content
///     "meta": {
///         "embeds": [
///             {
///                 "type": "link",
///                 "title: "...",
///                 /// ...
///             }
///         ]
///     }
/// }
/// </code>
/// </summary>
public abstract class EmbeddableBase
{
    public abstract string Type { get; }

    public static Dictionary<string, object> ToDictionary(dynamic input)
    {
        var jsonRaw = JsonSerializer.Serialize(
            input,
            GrpcTypeHelper.SerializerOptionsWithoutIgnore
        );
        return JsonSerializer.Deserialize<Dictionary<string, object>>(
            jsonRaw,
            GrpcTypeHelper.SerializerOptionsWithoutIgnore
        );
    }
}