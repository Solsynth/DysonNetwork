namespace DysonNetwork.Sphere.Connection.WebReader;

/// <summary>
/// The embeddable can be used in the post or messages' meta's embeds fields
/// To render richer type of content.
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
public interface IEmbeddable
{
    public string Type { get; }
}