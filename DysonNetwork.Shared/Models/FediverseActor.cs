using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(Uri), nameof(DeletedAt), IsUnique = true)]
public class SnFediverseActor : ModelBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(2048)]
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Person";

    [MaxLength(2048)]
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = null!;

    [MaxLength(256)]
    [JsonPropertyName("username")]
    public string Username { get; set; } = null!;

    [MaxLength(2048)]
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [MaxLength(4096)]
    [JsonPropertyName("bio")]
    public string? Bio { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("inbox_uri")]
    public string? InboxUri { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("outbox_uri")]
    public string? OutboxUri { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("followers_uri")]
    public string? FollowersUri { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("following_uri")]
    public string? FollowingUri { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("featured_uri")]
    public string? FeaturedUri { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("public_key_id")]
    public string? PublicKeyId { get; set; }

    [MaxLength(8192)]
    [JsonPropertyName("public_key")]
    public string? PublicKey { get; set; }

    [Column(TypeName = "jsonb")]
    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [MaxLength(2048)]
    [JsonPropertyName("header_url")]
    public string? HeaderUrl { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; } = false;

    [JsonPropertyName("is_locked")]
    public bool IsLocked { get; set; } = false;

    [JsonPropertyName("is_discoverable")]
    public bool IsDiscoverable { get; set; } = true;

    [JsonPropertyName("is_community")]
    public bool IsCommunity { get; set; } = false;

    [JsonPropertyName("realm_id")]
    public Guid? RealmId { get; set; }

    [JsonPropertyName("instance_id")]
    public Guid InstanceId { get; set; }

    [JsonPropertyName("instance")]
    public SnFediverseInstance Instance { get; set; } = null!;

    [JsonIgnore]
    public List<SnFediverseRelationship> FollowingRelationships { get; set; } = [];

    [JsonIgnore]
    public List<SnFediverseRelationship> FollowerRelationships { get; set; } = [];

    [JsonPropertyName("last_fetched_at")]
    public Instant? LastFetchedAt { get; set; }

    [JsonPropertyName("last_activity_at")]
    public Instant? LastActivityAt { get; set; }

    [JsonPropertyName("outbox_fetched_at")]
    public Instant? OutboxFetchedAt { get; set; }

    [JsonPropertyName("publisher_id")]
    public Guid? PublisherId { get; set; }

    [NotMapped]
    [JsonPropertyName("full_handle")]
    public string FullHandle => $"{Username}@{Instance?.Domain}";

    [NotMapped]
    [JsonPropertyName("web_url")]
    public string WebUrl
    {
        get
        {
            if (!string.IsNullOrEmpty(Uri) && Uri.StartsWith("http"))
            {
                return Uri.Replace("/users/", "/@");
            }
            var domain = Instance?.Domain ?? "localhost";
            return $"https://{domain}/@{Username}";
        }
    }

    [NotMapped]
    [JsonPropertyName("followers_count")]
    public int FollowersCount { get; set; }

    [NotMapped]
    [JsonPropertyName("following_count")]
    public int FollowingCount { get; set; }

    [NotMapped]
    [JsonPropertyName("post_count")]
    public int PostCount { get; set; }

    [NotMapped]
    [JsonPropertyName("total_post_count")]
    public int? TotalPostCount { get; set; }
}
