using System.Text.Json;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub.Services;

/// <summary>
/// Converts ActivityStreams <c>attachment</c> values into <see cref="SnCloudFileReferenceObject"/>
/// so federated posts expose remote media the same way local posts do.
/// Accepts both raw <see cref="JsonElement"/> values and the dictionary/list shapes produced by
/// <c>ActivityHandlerService.GetObject</c>, since the inbox body and the outbox fetch parse JSON
/// differently.
/// </summary>
public static class ActivityPubAttachmentConverter
{
    private const string DefaultMimeType = "application/octet-stream";

    public static List<SnCloudFileReferenceObject> FromActivityStream(object? attachment)
    {
        var files = new List<SnCloudFileReferenceObject>();
        Collect(attachment, files);
        return files;
    }

    private static void Collect(object? value, List<SnCloudFileReferenceObject> files)
    {
        switch (value)
        {
            case null:
                return;
            case string url when !string.IsNullOrWhiteSpace(url):
                files.Add(Create(new Dictionary<string, object?> { ["url"] = url }));
                return;
            case JsonElement element:
                CollectJson(element, files);
                return;
            case Dictionary<string, object?> dict:
                files.Add(Create(dict));
                return;
            case IEnumerable<object?> items:
                foreach (var item in items)
                    Collect(item, files);
                return;
        }
    }

    private static void CollectJson(JsonElement element, List<SnCloudFileReferenceObject> files)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectJson(item, files);
                return;
            case JsonValueKind.Object:
                files.Add(Create(ToDictionary(element)));
                return;
            case JsonValueKind.String:
                var url = element.GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    files.Add(Create(new Dictionary<string, object?> { ["url"] = url }));
                return;
        }
    }

    private static SnCloudFileReferenceObject Create(Dictionary<string, object?> dict)
    {
        var url = ResolveUrl(dict.GetValueOrDefault("url"))
            ?? ResolveUrl(dict.GetValueOrDefault("href"));
        var now = SystemClock.Instance.GetCurrentInstant();

        return new SnCloudFileReferenceObject
        {
            Id = Guid.NewGuid().ToString(),
            Name = GetString(dict, "name")
                ?? GetString(dict, "summary")
                ?? url
                ?? string.Empty,
            Url = url,
            // Clients require non-null mime/hash; ActivityPub Document attachments
            // often omit both, so fill safe defaults for remote posts.
            MimeType = GetString(dict, "mediaType")
                ?? GetString(dict, "mimeType")
                ?? DefaultMimeType,
            Hash = GetString(dict, "hash") ?? string.Empty,
            Width = GetInt(dict, "width"),
            Height = GetInt(dict, "height"),
            Blurhash = GetString(dict, "blurhash"),
            Size = GetLong(dict, "size") ?? 0,
            FileMeta = new Dictionary<string, object?>(),
            UserMeta = new Dictionary<string, object?>(),
            HasCompression = false,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// ActivityStreams <c>url</c> is a <c>Link | Link[]</c>: peers send a plain string, a
    /// <c>{ "href": ... }</c> object, or an array of either.
    /// </summary>
    private static string? ResolveUrl(object? value)
    {
        return value switch
        {
            string url => string.IsNullOrWhiteSpace(url) ? null : url,
            JsonElement element => ResolveUrl(ToValue(element)),
            Dictionary<string, object?> dict =>
                ResolveUrl(dict.GetValueOrDefault("href")) ?? ResolveUrl(dict.GetValueOrDefault("url")),
            IEnumerable<object?> items => items.Select(ResolveUrl).FirstOrDefault(url => url != null),
            _ => null
        };
    }

    private static Dictionary<string, object?> ToDictionary(JsonElement element)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var property in element.EnumerateObject())
            dict[property.Name] = ToValue(property.Value);
        return dict;
    }

    private static object? ToValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var value) ? value : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => ToDictionary(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ToValue).ToList(),
            _ => null
        };
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        return dict.GetValueOrDefault(key) switch
        {
            string value => string.IsNullOrWhiteSpace(value) ? null : value,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
    }

    private static int? GetInt(Dictionary<string, object?> dict, string key)
    {
        return dict.GetValueOrDefault(key) switch
        {
            int value => value,
            long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            double value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var parsed) => parsed,
            _ => null
        };
    }

    private static long? GetLong(Dictionary<string, object?> dict, string key)
    {
        return dict.GetValueOrDefault(key) switch
        {
            int value => value,
            long value => value,
            double value => (long)value,
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var parsed) => parsed,
            _ => null
        };
    }
}
