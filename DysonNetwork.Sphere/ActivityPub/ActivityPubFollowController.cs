using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

[ApiController]
[Route("/api/activitypub")]
[Authorize]
public class ActivityPubFollowController(
    AppDatabase db,
    ActivityPubDeliveryService deliveryService,
    ActivityPubDiscoveryService discSrv,
    IConfiguration configuration,
    ILogger<ActivityPubFollowController> logger
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpPost("follow")]
    public async Task<ActionResult> FollowRemoteUser([FromBody] FollowRequest request)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
            return Unauthorized(new { error = "Not authenticated" });

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == currentUser))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return BadRequest(new { error = "User doesn't have a publisher" });

        logger.LogInformation("User {UserId} wants to follow {TargetActor}",
            currentUser, request.TargetActorUri);

        var success = await deliveryService.SendFollowActivityAsync(
            publisher.Id,
            request.TargetActorUri
        );

        if (success)
        {
            return Ok(new
            {
                success = true,
                message = "Follow request sent. Waiting for acceptance.",
                targetActorUri = request.TargetActorUri
            });
        }

        return BadRequest(new { error = "Failed to send follow request" });
    }

    [HttpPost("unfollow")]
    public async Task<ActionResult> UnfollowRemoteUser([FromBody] UnfollowRequest request)
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
            return Unauthorized(new { error = "Not authenticated" });

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == currentUser))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return BadRequest(new { error = "User doesn't have a publisher" });

        var success = await deliveryService.SendUndoActivityAsync(
            "Follow",
            request.TargetActorUri,
            publisher.Id
        );

        if (success)
        {
            return Ok(new
            {
                success = true,
                message = "Unfollowed successfully"
            });
        }

        return BadRequest(new { error = "Failed to unfollow" });
    }

    [HttpGet("following")]
    public async Task<ActionResult<List<SnFediverseActor>>> GetFollowing(
        [FromQuery] int limit = 50
    )
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
            return Unauthorized();

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == currentUser))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return Ok(new List<SnFediverseActor>());

        var actors = await db.FediverseRelationships
            .Include(r => r.TargetActor)
            .Where(r =>
                r.IsLocalActor &&
                r.LocalPublisherId == publisher.Id &&
                r.IsFollowing &&
                r.State == RelationshipState.Accepted)
            .OrderByDescending(r => r.FollowedAt)
            .Select(r => r.TargetActor)
            .Take(limit)
            .ToListAsync();

        return Ok(actors);
    }

    [HttpGet("followers")]
    public async Task<ActionResult<List<SnFediverseActor>>> GetFollowers(
        [FromQuery] int limit = 50
    )
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
            return Unauthorized();

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == currentUser))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return Ok(new List<SnFediverseActor>());

        var actors = await db.FediverseRelationships
            .Include(r => r.Actor)
            .Where(r =>
                !r.IsLocalActor &&
                r.LocalPublisherId == publisher.Id &&
                r.IsFollowedBy &&
                r.State == RelationshipState.Accepted)
            .OrderByDescending(r => r.FollowedAt ?? r.CreatedAt)
            .Select(r => r.Actor)
            .Take(limit)
            .ToListAsync();

        return Ok(actors);
    }

    [HttpGet("search")]
    public async Task<ActionResult<List<SnFediverseActor>>> SearchRemoteUsers(
        [FromQuery] string query,
        [FromQuery] int limit = 20
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "Query is required" });

        var actors = await discSrv.SearchActorsAsync(query, limit, includeRemoteDiscovery: true);

        return Ok(actors);
    }

    [HttpGet("relationships")]
    public async Task<ActionResult<RelationshipsSummary>> GetRelationships()
    {
        var currentUser = GetCurrentUser();
        if (currentUser == null)
            return Unauthorized();

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == currentUser))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return NotFound(new { error = "Publisher not found" });

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";

        var followingCount = await db.FediverseRelationships
            .CountAsync(r =>
                r.IsLocalActor &&
                r.LocalPublisherId == publisher.Id &&
                r.IsFollowing &&
                r.State == RelationshipState.Accepted);

        var followersCount = await db.FediverseRelationships
            .CountAsync(r =>
                !r.IsLocalActor &&
                r.LocalPublisherId == publisher.Id &&
                r.IsFollowedBy &&
                r.State == RelationshipState.Accepted);

        var pendingCount = await db.FediverseRelationships
            .CountAsync(r =>
                r.IsLocalActor &&
                r.LocalPublisherId == publisher.Id &&
                r.State == RelationshipState.Pending);

        var relationships = await db.FediverseRelationships
            .Include(r => r.TargetActor)
            .Where(r => r.IsLocalActor && r.LocalPublisherId == publisher.Id)
            .OrderByDescending(r => r.FollowedAt ?? r.CreatedAt)
            .Take(20)
            .ToListAsync();

        return Ok(new RelationshipsSummary
        {
            ActorUri = actorUrl,
            FollowingCount = followingCount,
            FollowersCount = followersCount,
            PendingCount = pendingCount,
            Relationships = relationships.Select(r => new RelationshipSummaryItem
            {
                Actor = r.TargetActor,
                State = r.State,
                IsFollowing = r.IsFollowing,
                FollowedAt = r.FollowedAt,
                TargetActorUri = r.TargetActor.Uri,
                Username = r.TargetActor.Username,
                DisplayName = r.TargetActor.DisplayName
            }).ToList()
        });
    }

    [HttpGet("check/{username}")]
    [AllowAnonymous]
    public async Task<ActionResult<ActorCheckResult>> CheckActor(string username)
    {
        var actorUrl = GetActorUrl(username);

        var existingActor = await db.FediverseActors
            .Include(snFediverseActor => snFediverseActor.Instance)
            .FirstOrDefaultAsync(a => a.Uri == actorUrl);

        if (existingActor != null)
        {
            return Ok(new ActorCheckResult
            {
                Exists = true,
                Actor = existingActor,
                ActorUri = existingActor.Uri,
                Username = existingActor.Username,
                DisplayName = existingActor.DisplayName,
                Bio = existingActor.Bio,
                AvatarUrl = existingActor.AvatarUrl,
                InstanceDomain = existingActor.Instance.Domain,
                PublicKeyExists = !string.IsNullOrEmpty(existingActor.PublicKey),
                LastActivityAt = existingActor.LastActivityAt,
                IsLocal = false
            });
        }

        try
        {
            var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(actorUrl);

            if (!response.IsSuccessStatusCode)
            {
                return Ok(new ActorCheckResult
                {
                    Exists = false,
                    ActorUri = actorUrl,
                    Error = $"Actor not accessible: {response.StatusCode}"
                });
            }

            var json = await response.Content.ReadAsStringAsync();
            var actorData = System.Text.Json.JsonDocument.Parse(json);

            var preferredUsername = actorData.RootElement.GetProperty("preferredUsername").GetString();
            var displayName = actorData.RootElement.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString()
                : null;
            var bio = actorData.RootElement.TryGetProperty("summary", out var bioProp)
                ? bioProp.GetString()
                : null;
            var avatarUrl = actorData.RootElement.TryGetProperty("icon", out var iconProp)
                ? iconProp.GetProperty("url").GetString()
                : null;
            var publicKeyPem = actorData.RootElement.GetProperty("publicKey")
                .GetProperty("publicKeyPem").GetString();

            var domain = ExtractDomain(actorUrl);
            var instance = await db.FediverseInstances
                .FirstOrDefaultAsync(i => i.Domain == domain);

            if (instance == null)
            {
                instance = new SnFediverseInstance
                {
                    Domain = domain,
                    Name = domain
                };
                db.FediverseInstances.Add(instance);
                await db.SaveChangesAsync();
            }

            var actor = new SnFediverseActor
            {
                Uri = actorUrl,
                Username = username,
                DisplayName = displayName,
                Bio = bio,
                AvatarUrl = avatarUrl,
                PublicKey = publicKeyPem,
                InstanceId = instance.Id
            };

            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();

            return Ok(new ActorCheckResult
            {
                Exists = true,
                Actor = actor,
                ActorUri = actorUrl,
                Username = username,
                DisplayName = displayName,
                Bio = bio,
                AvatarUrl = avatarUrl,
                InstanceDomain = domain,
                PublicKeyExists = !string.IsNullOrEmpty(publicKeyPem),
                IsDiscoverable = true,
                IsLocal = false,
                LastActivityAt = SystemClock.Instance.GetCurrentInstant()
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to check actor {ActorUri}", actorUrl);
            return Ok(new ActorCheckResult
            {
                Exists = false,
                ActorUri = actorUrl,
                Error = ex.Message
            });
        }
    }

    private Guid? GetCurrentUser()
    {
        HttpContext.Items.TryGetValue("CurrentUser", out var currentUser);
        return currentUser as Guid?;
    }

    private string ExtractDomain(string actorUri)
    {
        var uri = new Uri(actorUri);
        return uri.Host;
    }

    private string GetActorUrl(string username)
    {
        return $"https://{Domain}/activitypub/actors/{username}";
    }
}

public class FollowRequest
{
    public string TargetActorUri { get; set; } = string.Empty;
}

public class UnfollowRequest
{
    public string TargetActorUri { get; set; } = string.Empty;
}

public class RelationshipsSummary
{
    public string ActorUri { get; set; } = string.Empty;
    public int FollowingCount { get; set; }
    public int FollowersCount { get; set; }
    public int PendingCount { get; set; }
    public List<RelationshipSummaryItem> Relationships { get; set; } = new();
}

public class RelationshipSummaryItem
{
    public SnFediverseActor Actor { get; set; } = null!;
    public RelationshipState State { get; set; }
    public bool IsFollowing { get; set; }
    public Instant? FollowedAt { get; set; }
    public string TargetActorUri { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
}

public class ActorCheckResult
{
    public bool Exists { get; set; }
    public SnFediverseActor? Actor { get; set; }
    public string ActorUri { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? InstanceDomain { get; set; }
    public bool PublicKeyExists { get; set; }
    public bool IsLocal { get; set; }
    public bool IsDiscoverable { get; set; }
    public Instant? LastActivityAt { get; set; }
    public string? Error { get; set; }
}