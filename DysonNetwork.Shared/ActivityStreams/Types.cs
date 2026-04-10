using System.Text.Json.Serialization;

namespace DysonNetwork.Shared.ActivityStreams;

public class ASObject
{
    [JsonPropertyName("@context")]
    public string[]? Context { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Object";

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("blurhash")]
    public string? Blurhash { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("published")]
    public DateTime? Published { get; set; }

    [JsonPropertyName("updated")]
    public DateTime? Updated { get; set; }

    [JsonPropertyName("attributedTo")]
    public string? AttributedTo { get; set; }

    [JsonPropertyName("actor")]
    public string? Actor { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("object")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Object { get; set; }

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("inReplyTo")]
    public string? InReplyTo { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("to")]
    public string[]? To { get; set; }

    [JsonPropertyName("cc")]
    public string[]? Cc { get; set; }

    [JsonPropertyName("bto")]
    public string[]? Bto { get; set; }

    [JsonPropertyName("bcc")]
    public string[]? Bcc { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime? StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTime? EndTime { get; set; }

    [JsonPropertyName("generator")]
    public ASObject? Generator { get; set; }

    [JsonPropertyName("icon")]
    public ASObject? Icon { get; set; }

    [JsonPropertyName("image")]
    public ASObject? Image { get; set; }

    [JsonPropertyName("attachment")]
    public ASObject[]? Attachment { get; set; }

    [JsonPropertyName("tag")]
    public ASObject[]? Tag { get; set; }

    [JsonPropertyName("replies")]
    public ASObject? Replies { get; set; }

    public bool IsActor => this is ASActor;
    public bool IsActivity => this is ASActivity;
    public ASActor? AsActor() => this as ASActor;
    public ASActivity? AsActivity() => this as ASActivity;
}

public class ASActor : ASObject
{
    [JsonPropertyName("preferredUsername")]
    public string? PreferredUsername { get; set; }

    [JsonPropertyName("inbox")]
    public string? Inbox { get; set; }

    [JsonPropertyName("outbox")]
    public string? Outbox { get; set; }

    [JsonPropertyName("followers")]
    public string? Followers { get; set; }

    [JsonPropertyName("following")]
    public string? Following { get; set; }

    [JsonPropertyName("liked")]
    public string? Liked { get; set; }

    [JsonPropertyName("streams")]
    public string[]? Streams { get; set; }

    [JsonPropertyName("publicKey")]
    public ASPublicKey? PublicKey { get; set; }

    [JsonPropertyName("endpoints")]
    public ASEndpoints? Endpoints { get; set; }

    [JsonPropertyName("featured")]
    public string? Featured { get; set; }

    [JsonPropertyName("featuredTags")]
    public string[]? FeaturedTags { get; set; }

    [JsonPropertyName("manuallyApprovesFollowers")]
    public bool? ManuallyApprovesFollowers { get; set; }

    [JsonPropertyName("discoverable")]
    public bool? Discoverable { get; set; }

    [JsonPropertyName("bot")]
    public bool? Bot { get; set; }

    [JsonPropertyName("alsoKnownAs")]
    public string[]? AlsoKnownAs { get; set; }

    [JsonPropertyName("movedTo")]
    public string? MovedTo { get; set; }

    [JsonPropertyName("suspended")]
    public bool? Suspended { get; set; }

    [JsonPropertyName("categories")]
    public string[]? Categories { get; set; }

    [JsonPropertyName("keywords")]
    public string[]? Keywords { get; set; }

    [JsonPropertyName("languages")]
    public string[]? Languages { get; set; }

    [JsonPropertyName("posts")]
    public string? Posts { get; set; }
}

public class ASPerson : ASActor
{
    [JsonPropertyName("day")]
    public string? Day { get; set; }

    [JsonPropertyName("born")]
    public string? Born { get; set; }

    [JsonPropertyName("pronouns")]
    public string[]? Pronouns { get; set; }
}

public class ASApplication : ASActor { }
public class ASGroup : ASActor { }
public class ASOrganization : ASActor { }
public class ASService : ASActor { }

public class ASPublicKey
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "RsaSignature2017";

    [JsonPropertyName("owner")]
    public string Owner { get; set; } = null!;

    [JsonPropertyName("publicKeyPem")]
    public string PublicKeyPem { get; set; } = null!;
}

public class ASEndpoints
{
    [JsonPropertyName("sharedInbox")]
    public string? SharedInbox { get; set; }
}

public class ASNote : ASObject
{
    [JsonPropertyName("quoteUrl")]
    public string? QuoteUrl { get; set; }

    [JsonPropertyName("quoteUri")]
    public string? QuoteUri { get; set; }

    [JsonPropertyName("quoteAuthorization")]
    public string? QuoteAuthorization { get; set; }

    [JsonPropertyName("quote")]
    public string? Quote { get; set; }

    [JsonPropertyName("sensitive")]
    public bool? Sensitive { get; set; }

    [JsonPropertyName("preview")]
    public ASObject? Preview { get; set; }
}

public class ASArticle : ASObject { }

public class ASImage : ASObject
{
    [JsonPropertyName("blurhash")]
    public string? Blurhash { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }
}

public class ASVideo : ASObject
{
    [JsonPropertyName("blurhash")]
    public string? Blurhash { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("duration")]
    public double? Duration { get; set; }
}

public class ASAudio : ASObject
{
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }
}

public class ASActivity : ASObject
{
    [JsonPropertyName("result")]
    public ASObject? Result { get; set; }

    [JsonPropertyName("instrument")]
    public ASObject? Instrument { get; set; }
}

public class ASCreate : ASActivity { }
public class ASUpdate : ASActivity { }
public class ASDelete : ASActivity { }
public class ASFollow : ASActivity { }
public class ASAccept : ASActivity { }
public class ASReject : ASActivity { }
public class ASAnnounce : ASActivity { }
public class ASLike : ASActivity { }
public class ASUndo : ASActivity { }
public class ASBlock : ASActivity { }
public class ASMove : ASActivity { }
public class ASFlag : ASActivity { }
public class ASAdd : ASActivity { }
public class ASRemove : ASActivity { }
public class ASJoin : ASActivity { }
public class ASLeave : ASActivity { }
public class ASQuoteRequest : ASActivity { }

public class ASTombstone : ASObject
{
    [JsonPropertyName("formerType")]
    public string? FormerType { get; set; }

    [JsonPropertyName("deleted")]
    public DateTime? Deleted { get; set; }
}

public class ASCollection : ASObject
{
    [JsonPropertyName("totalItems")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("current")]
    public string? Current { get; set; }

    [JsonPropertyName("first")]
    public string? First { get; set; }

    [JsonPropertyName("last")]
    public string? Last { get; set; }

    [JsonPropertyName("items")]
    public string[]? Items { get; set; }
}

public class ASOrderedCollection : ASObject
{
    [JsonPropertyName("totalItems")]
    public int? TotalItems { get; set; }

    [JsonPropertyName("first")]
    public string? First { get; set; }

    [JsonPropertyName("orderedItems")]
    public string[]? OrderedItems { get; set; }
}

public class ASCollectionPage : ASCollection
{
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("prev")]
    public string? Prev { get; set; }
}

public class ASOrderedCollectionPage : ASOrderedCollection
{
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    [JsonPropertyName("prev")]
    public string? Prev { get; set; }

    [JsonPropertyName("startIndex")]
    public int? StartIndex { get; set; }
}

public class ASPlace : ASObject { }
public class ASMention : ASObject { }
public class ASEmoji : ASObject { }
public class ASQuestion : ASObject { }