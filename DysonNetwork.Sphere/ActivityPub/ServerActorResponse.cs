using System.Text.Json.Serialization;

namespace DysonNetwork.Sphere.ActivityPub;

public class ServerActorResponse
{
    [JsonPropertyName("@context")]
    public object[] Context { get; init; } = [];

    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "Application";

    [JsonPropertyName("preferredUsername")]
    public string PreferredUsername { get; init; } = "server";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "DysonNetwork Server";

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = null!;

    [JsonPropertyName("url")]
    public string Url { get; init; } = null!;

    [JsonPropertyName("inbox")]
    public string Inbox { get; init; } = null!;

    [JsonPropertyName("outbox")]
    public string Outbox { get; init; } = null!;

    [JsonPropertyName("followers")]
    public string Followers { get; init; } = null!;

    [JsonPropertyName("publicKey")]
    public PublicKeyResponse PublicKey { get; init; } = null!;

    [JsonPropertyName("alsoKnownAs")]
    public string[] AlsoKnownAs { get; init; } = [];

    [JsonPropertyName("instanceActor")]
    public bool InstanceActor { get; init; } = true;
}

public class PublicKeyResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = null!;

    [JsonPropertyName("publicKeyPem")]
    public string PublicKeyPem { get; init; } = null!;
}

public class OrderedCollectionResponse
{
    [JsonPropertyName("@context")]
    public string Context { get; init; } = "https://www.w3.org/ns/activitystreams";

    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "OrderedCollection";

    [JsonPropertyName("totalItems")]
    public int TotalItems { get; init; }

    [JsonPropertyName("first")]
    public string First { get; init; } = null!;

    [JsonPropertyName("orderedItems")]
    public object[] OrderedItems { get; init; } = [];
}

public class PublicKeyDocumentResponse
{
    [JsonPropertyName("@context")]
    public object[] Context { get; init; } =
    ["https://w3id.org/security/v1", "https://www.w3.org/ns/activitystreams"];

    [JsonPropertyName("id")]
    public string Id { get; init; } = null!;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = null!;

    [JsonPropertyName("publicKeyPem")]
    public string PublicKeyPem { get; init; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "RsaSignature2017";
}
