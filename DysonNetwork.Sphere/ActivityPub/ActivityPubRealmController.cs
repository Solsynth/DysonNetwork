using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Npgsql;

namespace DysonNetwork.Sphere.ActivityPub;

[Route("activitypub/realms")]
public class ActivityPubRealmController(
    AppDatabase db,
    RemoteRealmService realmService,
    ActivityPubDeliveryService deliveryService,
    ActivityPubActivityHandler activityHandler,
    IConfiguration configuration,
    ILogger<ActivityPubRealmController> logger
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string AssetsBaseUrl => configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

    [HttpGet("{slug}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCommunityActor(string slug)
    {
        var realm = await realmService.GetRealmBySlug(slug);
        if (realm == null || !realm.IsCommunity)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/realms/{slug}";
        var inboxUrl = $"{actorUrl}/inbox";
        var followersUrl = $"{actorUrl}/followers";
        var outboxUrl = $"{actorUrl}/outbox";

        var existingActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.RealmId == realm.Id && a.IsCommunity);

        var publicKey = existingActor?.PublicKey;
        var publicKeyId = existingActor?.PublicKeyId;

        var actor = new ActivityPubActor
        {
            Context = new List<object> { "https://www.w3.org/ns/activitystreams", "https://w3id.org/security/v1" },
            Id = actorUrl,
            Type = "Group",
            Name = realm.Name,
            PreferredUsername = slug,
            Summary = realm.Description,
            Inbox = inboxUrl,
            Outbox = outboxUrl,
            Followers = followersUrl,
            Published = realm.CreatedAt,
            Url = $"https://{Domain}/realms/{slug}",
            Icon = realm.Picture != null
                ? new ActivityPubImage
                {
                    Type = "Image",
                    MediaType = realm.Picture.MimeType,
                    Url = $"{AssetsBaseUrl}/{realm.Picture.Id}"
                }
                : null,
            Image = realm.Background != null
                ? new ActivityPubImage
                {
                    Type = "Image",
                    MediaType = realm.Background.MimeType,
                    Url = $"{AssetsBaseUrl}/{realm.Background.Id}"
                }
                : null,
            PublicKey = !string.IsNullOrEmpty(publicKey) && !string.IsNullOrEmpty(publicKeyId)
                ? new ActivityPubPublicKey
                {
                    Id = publicKeyId,
                    Owner = actorUrl,
                    PublicKeyPem = publicKey
                }
                : null
        };

        return Ok(actor);
    }

    [HttpGet("{slug}/followers")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCommunityFollowers(string slug, [FromQuery] int? limit = 20)
    {
        var realm = await realmService.GetRealmBySlug(slug);
        if (realm == null || !realm.IsCommunity)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/realms/{slug}";

        var relationships = await db.FediverseRelationships
            .Where(r => r.RealmId == realm.Id && r.State == RelationshipState.Accepted)
            .Include(r => r.Actor)
            .Take(limit ?? 20)
            .ToListAsync();

        var items = relationships
            .Where(r => r.Actor != null)
            .Select(r => new ActivityPubActor
            {
                Id = r.Actor!.Uri,
                Type = "Person",
                Name = r.Actor.DisplayName ?? r.Actor.Username
            })
            .ToList();

        return Ok(new
        {
            @context = "https://www.w3.org/ns/activitystreams",
            id = $"{actorUrl}/followers",
            type = "OrderedCollection",
            totalItems = items.Count,
            orderedItems = items
        });
    }

    [HttpPost("{slug}/inbox")]
    [Consumes("application/activity+json")]
    [AllowAnonymous]
    public async Task<IActionResult> ReceiveCommunityActivity(string slug)
    {
        var realm = await realmService.GetRealmBySlug(slug);
        if (realm == null || !realm.IsCommunity)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/realms/{slug}";

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();

        Dictionary<string, object>? activity;
        try
        {
            activity = JsonSerializer.Deserialize<Dictionary<string, object>>(body);
        }
        catch
        {
            return BadRequest(new { error = "Invalid JSON" });
        }

        if (activity == null)
            return BadRequest(new { error = "Empty activity" });

        var activityType = activity.GetValueOrDefault("type")?.ToString();
        if (string.IsNullOrEmpty(activityType))
            return BadRequest(new { error = "No activity type" });

        var senderUri = activity.GetValueOrDefault("actor")?.ToString();
        if (string.IsNullOrEmpty(senderUri))
            return BadRequest(new { error = "No actor" });

        logger.LogInformation("Received {Type} activity for community {Slug} from {Actor}", activityType, slug, senderUri);

        switch (activityType)
        {
            case "Follow":
                return await HandleCommunityFollowAsync(realm, senderUri, activity);
            case "Undo":
                return await HandleCommunityUndoAsync(realm, senderUri, activity);
            case "Create":
            case "Note":
                return await HandleCommunityPostAsync(realm, senderUri, activity);
            default:
                logger.LogWarning("Unsupported activity type {Type} for community", activityType);
                return Ok(new { status = "ignored" });
        }
    }

    private async Task<IActionResult> HandleCommunityFollowAsync(SnRealm realm, string actorUri, Dictionary<string, object> activity)
    {
        var actor = await GetOrCreateActorAsync(actorUri);
        
        var existingFollow = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => 
                r.TargetActorId == actor.Id && 
                r.RealmId == realm.Id &&
                r.State == RelationshipState.Accepted);

        if (existingFollow != null)
        {
            return Ok(new { status = "already_following" });
        }

        var relationship = new SnFediverseRelationship
        {
            ActorId = actor.Id,
            TargetActorId = actor.Id,
            RealmId = realm.Id,
            State = RelationshipState.Accepted,
            FollowedAt = SystemClock.Instance.GetCurrentInstant()
        };

        db.FediverseRelationships.Add(relationship);
        await db.SaveChangesAsync();

        var communityActorUrl = $"https://{Domain}/activitypub/realms/{realm.Slug}";
        var acceptActivity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{communityActorUrl}/accepts/{Guid.NewGuid()}",
            ["type"] = "Accept",
            ["actor"] = communityActorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Follow",
                ["actor"] = actorUri,
                ["object"] = communityActorUrl
            }
        };

        var inboxUri = actor.InboxUri;
        if (!string.IsNullOrEmpty(inboxUri))
        {
            await deliveryService.EnqueueActivityDeliveryAsync(
                "Accept",
                acceptActivity,
                communityActorUrl,
                inboxUri,
                acceptActivity["id"]?.ToString() ?? ""
            );
        }

        logger.LogInformation("Accepted follow request from {Actor} for community {Slug}", actorUri, realm.Slug);
        return Accepted(new { status = "accepted" });
    }

    private async Task<IActionResult> HandleCommunityUndoAsync(SnRealm realm, string actorUri, Dictionary<string, object> activity)
    {
        var objectDict = activity.GetValueOrDefault("object") as Dictionary<string, object>;
        if (objectDict == null)
            return BadRequest(new { error = "No object in undo" });

        var objectType = objectDict.GetValueOrDefault("type")?.ToString();
        if (objectType != "Follow")
            return Ok(new { status = "ignored" });

        var targetUri = objectDict.GetValueOrDefault("object")?.ToString();
        if (targetUri != $"https://{Domain}/activitypub/realms/{realm.Slug}")
            return Ok(new { status = "ignored" });

        var actor = await GetOrCreateActorAsync(actorUri);
        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => 
                r.ActorId == actor.Id && 
                r.TargetActorId == actor.Id &&
                r.RealmId == realm.Id);

        if (relationship != null)
        {
            db.FediverseRelationships.Remove(relationship);
            await db.SaveChangesAsync();
            logger.LogInformation("Removed follow relationship for {Actor} from community {Slug}", actorUri, realm.Slug);
        }

        return Ok(new { status = "undone" });
    }

    private async Task<SnFediverseActor> GetOrCreateActorAsync(string actorUri)
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor != null) return actor;

        var uri = new Uri(actorUri);
        var instance = await db.FediverseInstances
            .FirstOrDefaultAsync(i => i.Domain == uri.Host);

        if (instance == null)
        {
            instance = new SnFediverseInstance { Domain = uri.Host };
            db.FediverseInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        actor = new SnFediverseActor
        {
            Uri = actorUri,
            Username = actorUri.Split('/').LastOrDefault() ?? "unknown",
            DisplayName = actorUri.Split('/').LastOrDefault() ?? "unknown",
            InstanceId = instance.Id
        };

        try
        {
            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            actor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Uri == actorUri);
        }

        return actor;
    }

    private async Task<IActionResult> HandleCommunityPostAsync(SnRealm realm, string actorUri, Dictionary<string, object> activity)
    {
        var communityActorUrl = $"https://{Domain}/activitypub/realms/{realm.Slug}";

        var announceActivity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{communityActorUrl}/announce/{Guid.NewGuid()}",
            ["type"] = "Announce",
            ["actor"] = communityActorUrl,
            ["object"] = activity,
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{communityActorUrl}/followers" }
        };

        var followers = await db.FediverseRelationships
            .Where(r => r.RealmId == realm.Id && r.State == RelationshipState.Accepted)
            .Include(r => r.Actor)
            .ToListAsync();

        foreach (var follower in followers)
        {
            if (follower.Actor?.InboxUri == null) continue;
            await deliveryService.EnqueueActivityDeliveryAsync(
                "Announce",
                announceActivity,
                communityActorUrl,
                follower.Actor.InboxUri,
                announceActivity["id"]?.ToString() ?? ""
            );
        }

        logger.LogInformation("Announced post from {Actor} to {Count} followers of community {Slug}", 
            actorUri, followers.Count, realm.Slug);

        return Accepted(new { status = "announced" });
    }

    [HttpGet("{slug}/outbox")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCommunityOutbox(string slug, [FromQuery] int? limit = 20)
    {
        var realm = await realmService.GetRealmBySlug(slug);
        if (realm == null || !realm.IsCommunity)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/realms/{slug}";

        var activities = await db.ActivityPubDeliveries
            .Where(d => d.ActorUri == actorUrl)
            .OrderByDescending(d => d.SentAt)
            .Take(limit ?? 20)
            .ToListAsync();

        var items = activities.Select(a => 
        {
            if (string.IsNullOrEmpty(a.ActivityPayload))
                return (object)new { id = a.ActivityId, type = a.ActivityType };
            try
            {
                return JsonSerializer.Deserialize<object>(a.ActivityPayload) ?? new { id = a.ActivityId, type = a.ActivityType };
            }
            catch
            {
                return new { id = a.ActivityId, type = a.ActivityType };
            }
        }).ToList();

        return Ok(new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/outbox",
            ["type"] = "OrderedCollection",
            ["totalItems"] = items.Count,
            ["orderedItems"] = items
        });
    }
}

public enum FediverseRelationshipStatus
{
    Pending,
    Accepted,
    Rejected
}