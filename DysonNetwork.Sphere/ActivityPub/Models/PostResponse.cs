using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class BoostInfo
{
    [JsonPropertyName("boost_id")]
    public Guid BoostId { get; set; }

    [JsonPropertyName("boosted_at")]
    public Instant BoostedAt { get; set; }

    [JsonPropertyName("activity_pub_uri")]
    public string? ActivityPubUri { get; set; }

    [JsonPropertyName("web_url")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("original_post")]
    public SnPost OriginalPost { get; set; } = null!;

    [JsonPropertyName("original_actor")]
    public SnFediverseActor? OriginalActor { get; set; }
}

public class PostResponse : SnPost
{
    [JsonPropertyName("boost_info")]
    public BoostInfo? BoostInfo { get; set; }

    /// <summary>
    /// Whether this post is stored locally in the database.
    /// If true, GET /posts/{id} will return this post.
    /// If false, the post is only fetched from remote outbox.
    /// </summary>
    [JsonPropertyName("is_cached")]
    public bool IsCached { get; set; }
}
