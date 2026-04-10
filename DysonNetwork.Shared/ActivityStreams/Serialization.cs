using System.Text.Json;
using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.ActivityStreams;

public class ASDeserializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ASObject? Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return Deserialize(doc.RootElement);
    }

    public static ASObject? Deserialize(JsonElement element)
    {
        if (!element.TryGetProperty("type", out var typeProp))
            return null;

        var type = typeProp.GetString();
        if (string.IsNullOrEmpty(type))
            return null;

        return type switch
        {
            "Person" => JsonSerializer.Deserialize<ASPerson>(element, Options),
            "Application" => JsonSerializer.Deserialize<ASApplication>(element, Options),
            "Group" => JsonSerializer.Deserialize<ASGroup>(element, Options),
            "Organization" => JsonSerializer.Deserialize<ASOrganization>(element, Options),
            "Service" => JsonSerializer.Deserialize<ASService>(element, Options),
            "Note" => JsonSerializer.Deserialize<ASNote>(element, Options),
            "Image" => JsonSerializer.Deserialize<ASImage>(element, Options),
            "Video" => JsonSerializer.Deserialize<ASVideo>(element, Options),
            "Audio" => JsonSerializer.Deserialize<ASAudio>(element, Options),
            "Collection" => JsonSerializer.Deserialize<ASCollection>(element, Options),
            "OrderedCollection" => JsonSerializer.Deserialize<ASOrderedCollection>(element, Options),
            "CollectionPage" => JsonSerializer.Deserialize<ASCollectionPage>(element, Options),
            "OrderedCollectionPage" => JsonSerializer.Deserialize<ASOrderedCollectionPage>(element, Options),
            "Tombstone" => JsonSerializer.Deserialize<ASTombstone>(element, Options),
            "Place" => JsonSerializer.Deserialize<ASPlace>(element, Options),
            "Mention" => JsonSerializer.Deserialize<ASMention>(element, Options),
            "Emoji" => JsonSerializer.Deserialize<ASEmoji>(element, Options),
            "Question" => JsonSerializer.Deserialize<ASQuestion>(element, Options),
            "Create" => JsonSerializer.Deserialize<ASCreate>(element, Options),
            "Update" => JsonSerializer.Deserialize<ASUpdate>(element, Options),
            "Delete" => JsonSerializer.Deserialize<ASDelete>(element, Options),
            "Follow" => JsonSerializer.Deserialize<ASFollow>(element, Options),
            "Accept" => JsonSerializer.Deserialize<ASAccept>(element, Options),
            "Reject" => JsonSerializer.Deserialize<ASReject>(element, Options),
            "Announce" => JsonSerializer.Deserialize<ASAnnounce>(element, Options),
            "Like" => JsonSerializer.Deserialize<ASLike>(element, Options),
            "Block" => JsonSerializer.Deserialize<ASBlock>(element, Options),
            "Move" => JsonSerializer.Deserialize<ASMove>(element, Options),
            "Flag" => JsonSerializer.Deserialize<ASFlag>(element, Options),
            "Undo" => JsonSerializer.Deserialize<ASUndo>(element, Options),
            "QuoteRequest" => JsonSerializer.Deserialize<ASQuoteRequest>(element, Options),
            _ => JsonSerializer.Deserialize<ASActivity>(element, Options)
        };
    }

    public static Dictionary<string, object?>? ToDictionary(ASObject? obj)
    {
        if (obj == null) return null;

        var json = JsonSerializer.Serialize(obj, Options);
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, Options);
    }

    public static string Serialize(ASObject obj)
    {
        return JsonSerializer.Serialize(obj, Options);
    }
}

public class ASSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(ASObject obj)
    {
        return JsonSerializer.Serialize(obj, Options);
    }

    public static string Serialize<T>(T obj) where T : ASObject
    {
        return JsonSerializer.Serialize(obj, Options);
    }
}

public static class ASObjectExtensions
{
    public static string Compact(this ASObject obj)
    {
        var clone = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(obj.Id))
            clone["id"] = obj.Id;
        
        if (!string.IsNullOrEmpty(obj.Type))
            clone["type"] = obj.Type;

        if (!string.IsNullOrEmpty(obj.AttributedTo))
            clone["attributedTo"] = obj.AttributedTo;

        if (!string.IsNullOrEmpty(obj.Actor))
            clone["actor"] = obj.Actor;

        if (obj.Object is string objStr && !string.IsNullOrEmpty(objStr))
            clone["object"] = objStr;
        else if (obj.Object != null)
            clone["object"] = obj.Object;

        if (!string.IsNullOrEmpty(obj.Target))
            clone["target"] = obj.Target;

        if (!string.IsNullOrEmpty(obj.InReplyTo))
            clone["inReplyTo"] = obj.InReplyTo;

        if (!string.IsNullOrEmpty(obj.Url))
            clone["url"] = obj.Url;

        if (obj.To != null && obj.To.Length > 0)
            clone["to"] = obj.To;

        if (obj.Cc != null && obj.Cc.Length > 0)
            clone["cc"] = obj.Cc;

        if (!string.IsNullOrEmpty(obj.Name))
            clone["name"] = obj.Name;

        if (!string.IsNullOrEmpty(obj.Summary))
            clone["summary"] = obj.Summary;

        if (!string.IsNullOrEmpty(obj.Content))
            clone["content"] = obj.Content;

        if (obj.Published.HasValue)
            clone["published"] = obj.Published;

        if (obj.Updated.HasValue)
            clone["updated"] = obj.Updated;

        return JsonSerializer.Serialize(clone);
    }

    public static bool IsActor(this ASObject obj)
    {
        return obj is ASActor;
    }

    public static bool IsActivity(this ASObject obj)
    {
        return obj is ASActivity;
    }

    public static bool IsObject(this ASObject obj)
    {
        return obj is ASObject and not ASActivity and not ASActor;
    }

    public static ASActor? AsActor(this ASObject obj)
    {
        return obj as ASActor;
    }

    public static ASActivity? AsActivity(this ASObject obj)
    {
        return obj as ASActivity;
    }

    public static string GetId(this ASObject obj)
    {
        return obj.Id ?? "";
    }

    public static string GetType(this ASObject obj)
    {
        return obj.Type ?? "";
    }

    public static IEnumerable<string> GetAddresses(this ASObject obj)
    {
        var addresses = new List<string>();
        
        if (obj.To != null)
            addresses.AddRange(obj.To);
        if (obj.Cc != null)
            addresses.AddRange(obj.Cc);
        if (obj.Bto != null)
            addresses.AddRange(obj.Bto);
        if (obj.Bcc != null)
            addresses.AddRange(obj.Bcc);

        return addresses.Distinct();
    }

    public static bool HasAddress(this ASObject obj, string address)
    {
        return obj.GetAddresses().Contains(address);
    }

    public static bool IsPublic(this ASObject obj)
    {
        const string publicAddress = "https://www.w3.org/ns/activitystreams#Public";
        return obj.HasAddress(publicAddress);
    }

    public static bool IsFollowerCollection(this ASObject obj)
    {
        return obj is ASCollection or ASOrderedCollection;
    }

    public static bool IsCollectionPage(this ASObject obj)
    {
        return obj is ASCollectionPage or ASOrderedCollectionPage;
    }

    public static IEnumerable<string> GetItems(this ASCollection collection)
    {
        return collection.Items ?? Array.Empty<string>();
    }

    public static IEnumerable<string> GetOrderedItems(this ASOrderedCollection collection)
    {
        return collection.OrderedItems ?? Array.Empty<string>();
    }
}