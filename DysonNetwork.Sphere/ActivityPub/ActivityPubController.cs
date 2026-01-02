using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Swashbuckle.AspNetCore.Annotations;

namespace DysonNetwork.Sphere.ActivityPub;

[ApiController]
[Route("activitypub/actors/{username}")]
public class ActivityPubController(
    AppDatabase db,
    IConfiguration configuration,
    ILogger<ActivityPubController> logger,
    ActivityPubSignatureService signatureService,
    ActivityPubActivityHandler activityHandler,
    ActivityPubKeyService keyService,
    ActivityPubObjectFactory objFactory
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpGet("")]
    [Produces("application/activity+json")]
    [ProducesResponseType(typeof(ActivityPubActor), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [SwaggerOperation(
        Summary = "Get ActivityPub actor",
        Description = "Returns the ActivityPub actor (user) profile in JSON-LD format",
        OperationId = "GetActivityPubActor"
    )]
    public async Task<ActionResult<ActivityPubActor>> GetActor(string username)
    {
        var publisher = await db.Publishers
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Name == username);

        if (publisher == null)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/actors/{username}";
        var inboxUrl = $"{actorUrl}/inbox";
        var outboxUrl = $"{actorUrl}/outbox";
        var followersUrl = $"{actorUrl}/followers";
        var followingUrl = $"{actorUrl}/following";
        var assetsBaseUrl = configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

        var publicKeyPem = await GetPublicKeyAsync(publisher);

        var actor = new ActivityPubActor
        {
            Context = ["https://www.w3.org/ns/activitystreams", "https://w3id.org/security/v1"],
            Id = actorUrl,
            Type = "Person",
            Name = publisher.Nick,
            PreferredUsername = publisher.Name,
            Summary = publisher.Bio,
            Inbox = inboxUrl,
            Outbox = outboxUrl,
            Followers = followersUrl,
            Following = followingUrl,
            Published = publisher.CreatedAt,
            Url = $"https://{Domain}/users/{publisher.Name}",
            Icon = publisher.Picture != null
                ? new ActivityPubImage
                {
                    Type = "Image",
                    MediaType = publisher.Picture.MimeType,
                    Url = $"{assetsBaseUrl}/{publisher.Picture.Id}"
                }
                : null,
            Image = publisher.Background != null
                ? new ActivityPubImage
                {
                    Type = "Image",
                    MediaType = publisher.Background.MimeType,
                    Url = $"{assetsBaseUrl}/{publisher.Background.Id}"
                }
                : null,
            PublicKey = new ActivityPubPublicKey
            {
                Id = $"{actorUrl}#main-key",
                Owner = actorUrl,
                PublicKeyPem = publicKeyPem
            }
        };

        return Ok(actor);
    }

    [HttpPost("inbox")]
    [Consumes("application/activity+json")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [SwaggerOperation(
        Summary = "Receive ActivityPub activities",
        Description = "Endpoint for receiving ActivityPub activities (Create, Follow, Like, etc.) from remote servers",
        OperationId = "ReceiveActivity"
    )]
    public async Task<IActionResult> PostInbox(string username, [FromBody] Dictionary<string, object> activity)
    {
        if (!signatureService.VerifyIncomingRequest(HttpContext, out var actorUri))
        {
            logger.LogWarning("Failed to verify signature for incoming activity");
            return Unauthorized(new { error = "Invalid signature" });
        }

        var success = await activityHandler.HandleIncomingActivityAsync(HttpContext, username, activity);

        if (!success)
        {
            logger.LogWarning("Failed to process activity for actor {Username}", username);
            return BadRequest(new { error = "Failed to process activity" });
        }

        logger.LogInformation("Successfully processed activity for actor {Username}: {Type}", username,
            activity.GetValueOrDefault("type")?.ToString());

        return Accepted();
    }

    [HttpGet("outbox")]
    [Produces("application/activity+json")]
    [ProducesResponseType(typeof(ActivityPubCollection), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ActivityPubCollectionPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [SwaggerOperation(
        Summary = "Get ActivityPub outbox",
        Description = "Returns the actor's outbox collection containing their public activities",
        OperationId = "GetActorOutbox"
    )]
    public async Task<IActionResult> GetOutbox(string username, [FromQuery] int? page)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name == username);

        if (publisher == null)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/actors/{username}";
        var outboxUrl = $"{actorUrl}/outbox";

        var postsQuery = db.Posts
            .Where(p => p.PublisherId == publisher.Id && p.Visibility == PostVisibility.Public);

        var totalItems = await postsQuery.CountAsync();

        if (page.HasValue)
        {
            const int pageSize = 20;
            var skip = (page.Value - 1) * pageSize;

            var posts = await postsQuery
                .OrderByDescending(p => p.PublishedAt ?? p.CreatedAt)
                .Skip(skip)
                .Take(pageSize)
                .ToListAsync();

            var items = Task.WhenAll(posts.Select(async post =>
            {
                var postObject = await objFactory.CreatePostObject(post, actorUrl);
                postObject["url"] = $"https://{Domain}/posts/{post.Id}";
                return new Dictionary<string, object>
                {
                    ["id"] = $"https://{Domain}/activitypub/objects/{post.Id}/activity",
                    ["type"] = "Create",
                    ["actor"] = actorUrl,
                    ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
                    ["to"] = new[] { ActivityPubObjectFactory.PublicTo },
                    ["cc"] = new[] { $"{actorUrl}/followers" },
                    ["@object"] = postObject
                };
            })).Result.Cast<object>().ToList();

            var collectionPage = new ActivityPubCollectionPage
            {
                Context = ["https://www.w3.org/ns/activitystreams"],
                Id = $"{outboxUrl}?page={page.Value}",
                Type = "OrderedCollectionPage",
                TotalItems = totalItems,
                PartOf = outboxUrl,
                OrderedItems = items,
                Next = skip + pageSize < totalItems ? $"{outboxUrl}?page={page.Value + 1}" : null,
                Prev = page.Value > 1 ? $"{outboxUrl}?page={page.Value - 1}" : null
            };

            return Ok(collectionPage);
        }
        else
        {
            var collection = new ActivityPubCollection
            {
                Context = ["https://www.w3.org/ns/activitystreams"],
                Id = outboxUrl,
                Type = "OrderedCollection",
                TotalItems = totalItems,
                First = $"{outboxUrl}?page=1"
            };

            return Ok(collection);
        }
    }

    [HttpGet("followers")]
    [Produces("application/activity+json")]
    [ProducesResponseType(typeof(ActivityPubCollection), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ActivityPubCollectionPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [SwaggerOperation(
        Summary = "Get ActivityPub followers",
        Description = "Returns the actor's followers collection with pagination support",
        OperationId = "GetActorFollowers"
    )]
    public async Task<IActionResult> GetFollowers(string username, [FromQuery] int? page)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name == username);

        if (publisher == null)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/actors/{username}";
        var followersUrl = $"{actorUrl}/followers";

        var relationshipsQuery = db.FediverseRelationships
            .Include(r => r.Actor)
            .Where(r => r.TargetActor.PublisherId == publisher.Id && r.State == RelationshipState.Accepted);

        var totalItems = await relationshipsQuery.CountAsync();

        if (page.HasValue)
        {
            const int pageSize = 40;
            var skip = (page.Value - 1) * pageSize;

            var actorUris = await relationshipsQuery
                .OrderByDescending(r => r.FollowedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(r => r.Actor.Uri)
                .ToListAsync();

            var collectionPage = new ActivityPubCollectionPage
            {
                Context = ["https://www.w3.org/ns/activitystreams"],
                Id = $"{followersUrl}?page={page.Value}",
                Type = "OrderedCollectionPage",
                TotalItems = totalItems,
                PartOf = followersUrl,
                OrderedItems = actorUris.Cast<object>().ToList()
            };

            return Ok(collectionPage);
        }
        else
        {
            var collection = new ActivityPubCollection
            {
                Context = ["https://www.w3.org/ns/activitystreams"],
                Id = followersUrl,
                Type = "OrderedCollection",
                TotalItems = totalItems,
                First = $"{followersUrl}?page=1"
            };

            return Ok(collection);
        }
    }

    [HttpGet("following")]
    [Produces("application/activity+json")]
    [ProducesResponseType(typeof(ActivityPubCollection), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ActivityPubCollectionPage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [SwaggerOperation(
        Summary = "Get ActivityPub following",
        Description = "Returns the actors that this actor follows with pagination support",
        OperationId = "GetActorFollowing"
    )]
    public async Task<IActionResult> GetFollowing(string username, [FromQuery] int? page)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Name == username);

        if (publisher == null)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/actors/{username}";
        var followingUrl = $"{actorUrl}/following";

        var relationshipsQuery = db.FediverseRelationships
            .Include(r => r.TargetActor)
            .Where(r => r.Actor.PublisherId == publisher.Id && r.State == RelationshipState.Accepted);

        var totalItems = await relationshipsQuery.CountAsync();

        if (page.HasValue)
        {
            const int pageSize = 40;
            var skip = (page.Value - 1) * pageSize;

            var actorUris = await relationshipsQuery
                .OrderByDescending(r => r.FollowedAt)
                .Skip(skip)
                .Take(pageSize)
                .Select(r => r.TargetActor.Uri)
                .ToListAsync();

            var collectionPage = new ActivityPubCollectionPage
            {
                Context = ["https://www.w3.org/ns/activitystreams"],
                Id = $"{followingUrl}?page={page.Value}",
                Type = "OrderedCollectionPage",
                TotalItems = totalItems,
                PartOf = followingUrl,
                OrderedItems = actorUris.Cast<object>().ToList()
            };

            return Ok(collectionPage);
        }
        else
        {
            var collection = new ActivityPubCollection
            {
                Context = ["https://www.w3.org/ns/activitystreams"],
                Id = followingUrl,
                Type = "OrderedCollection",
                TotalItems = totalItems,
                First = $"{followingUrl}?page=1"
            };

            return Ok(collection);
        }
    }

    private async Task<string> GetPublicKeyAsync(SnPublisher publisher)
    {
        var publicKeyPem = GetPublisherKey(publisher, "public_key");

        if (!string.IsNullOrEmpty(publicKeyPem))
        {
            logger.LogInformation("Using existing public key for publisher: {PublisherId}", publisher.Id);
            return publicKeyPem;
        }

        logger.LogInformation("Generating new key pair for publisher: {PublisherId} ({Name})",
            publisher.Id, publisher.Name);

        var (newPrivate, newPublic) = keyService.GenerateKeyPair();
        SavePublisherKey(publisher, "private_key", newPrivate);
        SavePublisherKey(publisher, "public_key", newPublic);

        db.Update(publisher);
        await db.SaveChangesAsync();

        logger.LogInformation("Saved new key pair to database for publisher: {PublisherId}", publisher.Id);

        return newPublic;
    }

    private static string? GetPublisherKey(SnPublisher publisher, string keyName)
    {
        return keyName switch
        {
            "private_key" => publisher.PrivateKeyPem,
            "public_key" => publisher.PublicKeyPem,
            _ => null
        };
    }

    private static void SavePublisherKey(SnPublisher publisher, string keyName, string keyValue)
    {
        switch (keyName)
        {
            case "private_key":
                publisher.PrivateKeyPem = keyValue;
                break;
            case "public_key":
                publisher.PublicKeyPem = keyValue;
                break;
        }
    }
}

public class ActivityPubActor
{
    [JsonPropertyName("@context")] public List<object> Context { get; set; } = [];
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("preferredUsername")]
    public string? PreferredUsername { get; set; }

    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("inbox")] public string? Inbox { get; set; }
    [JsonPropertyName("outbox")] public string? Outbox { get; set; }
    [JsonPropertyName("followers")] public string? Followers { get; set; }
    [JsonPropertyName("following")] public string? Following { get; set; }
    [JsonPropertyName("published")] public Instant? Published { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
    [JsonPropertyName("icon")] public ActivityPubImage? Icon { get; set; }
    [JsonPropertyName("image")] public ActivityPubImage? Image { get; set; }
    [JsonPropertyName("publicKey")] public ActivityPubPublicKey? PublicKey { get; set; }
}

public class ActivityPubPublicKey
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("owner")] public string? Owner { get; set; }
    [JsonPropertyName("publicKeyPem")] public string? PublicKeyPem { get; set; }
}

public class ActivityPubImage
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("mediaType")] public string? MediaType { get; set; }
    [JsonPropertyName("url")] public string? Url { get; set; }
}

public class ActivityPubCollection
{
    [JsonPropertyName("@context")] public List<object> Context { get; set; } = [];
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("totalItems")] public int TotalItems { get; set; }
    [JsonPropertyName("first")] public string? First { get; set; }
    [JsonPropertyName("items")] public List<object>? Items { get; set; }
}

public class ActivityPubCollectionPage
{
    [JsonPropertyName("@context")] public List<object> Context { get; set; } = [];
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("totalItems")] public int TotalItems { get; set; }
    [JsonPropertyName("partOf")] public string? PartOf { get; set; }
    [JsonPropertyName("orderedItems")] public List<object>? OrderedItems { get; set; }
    [JsonPropertyName("next")] public string? Next { get; set; }
    [JsonPropertyName("prev")] public string? Prev { get; set; }
}