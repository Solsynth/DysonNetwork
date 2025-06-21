using System.Reflection;
using System.Text.Json.Serialization;

namespace DysonNetwork.Sphere.Connection.WebReader;

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

    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>();
        foreach (var prop in GetType().GetProperties())
        {
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                continue;
            var value = prop.GetValue(this);
            if (value is null) continue;
            dict[prop.Name] = value;
        }

        return dict;
    }
}