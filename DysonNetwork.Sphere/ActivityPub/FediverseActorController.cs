using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

[ApiController]
[Route("/api/fediverse/actors")]
public class FediverseActorController(
    AppDatabase db,
    ActivityPubDiscoveryService discoveryService,
    FediverseCachingService cachingService,
    IConfiguration configuration,
    ILogger<FediverseActorController> logger
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpGet("{username}@{instance}")]
    [AllowAnonymous]
    public async Task<ActionResult<FediverseActorResponse>> GetActorByHandle(
        string username,
        string instance,
        [FromQuery] bool includeActivity = false
    )
    {
        var cachedActor = await cachingService.GetActorByHandleAsync(username, instance);
        if (cachedActor != null)
        {
            var response = CachedActorToResponse(cachedActor);
            if (includeActivity)
            {
                response.RecentPosts = await GetActorPostsInternalAsync(cachedActor.Id, 5);
            }
            return Ok(response);
        }

        var dbActor = await cachingService.GetActorFromDbByHandleAsync(username, instance);
        if (dbActor == null)
        {
            var discoveredActor = await discoveryService.DiscoverActorAsync($"{username}@{instance}");
            if (discoveredActor == null)
                return NotFound(new { error = "Actor not found" });
            
            await cachingService.SetActorAsync(discoveredActor, instance);
            dbActor = await cachingService.GetActorByHandleAsync(username, instance);
            if (dbActor == null)
                return NotFound(new { error = "Actor not found" });
        }

        var dto = CachedActorToResponse(dbActor);

        if (includeActivity)
        {
            dto.RecentPosts = await GetActorPostsInternalAsync(dbActor.Id, 5);
        }

        return Ok(dto);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<FediverseActorResponse>> GetActorById(
        Guid id,
        [FromQuery] bool includeActivity = false
    )
    {
        var cachedActor = await cachingService.GetActorByIdAsync(id);
        if (cachedActor != null)
        {
            var response = CachedActorToResponse(cachedActor);
            if (includeActivity)
            {
                response.RecentPosts = await GetActorPostsInternalAsync(cachedActor.Id, 5);
            }
            return Ok(response);
        }

        var actor = await cachingService.GetActorFromDbByIdAsync(id);
        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var dto = CachedActorToResponse(actor);

        if (includeActivity)
        {
            dto.RecentPosts = await GetActorPostsInternalAsync(actor.Id, 5);
        }

        return Ok(dto);
    }

    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<List<FediverseActorResponse>>> SearchActors(
        [FromQuery] string query,
        [FromQuery] int limit = 20
    )
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { error = "Query is required" });

        limit = Math.Clamp(limit, 1, 50);

        var cachedResults = await cachingService.GetSearchResultsAsync(query, limit);
        if (cachedResults != null && cachedResults.Count > 0)
        {
            return Ok(cachedResults.Select(CachedActorToResponse).ToList());
        }

        var remoteActors = await discoveryService.SearchActorsAsync(query, limit, includeRemoteDiscovery: true);

        var actorDtos = new List<FediverseActorResponse>();
        var cachedActors = new List<CachedActor>();
        
        foreach (var actor in remoteActors)
        {
            var dto = await ToDtoAsync(actor);
            actorDtos.Add(dto);
            
            cachedActors.Add(new CachedActor
            {
                Id = actor.Id,
                Type = actor.Type,
                Uri = actor.Uri,
                Username = actor.Username,
                DisplayName = actor.DisplayName,
                Bio = actor.Bio,
                AvatarUrl = actor.AvatarUrl,
                HeaderUrl = actor.HeaderUrl,
                IsBot = actor.IsBot,
                IsLocked = actor.IsLocked,
                IsDiscoverable = actor.IsDiscoverable,
                InstanceDomain = actor.Instance?.Domain,
                InstanceName = actor.Instance?.Name,
                InstanceSoftware = actor.Instance?.Software,
                Instance = actor.Instance != null ? new CachedInstance
                {
                    Id = actor.Instance.Id,
                    Domain = actor.Instance.Domain,
                    Name = actor.Instance.Name,
                    Description = actor.Instance.Description,
                    Software = actor.Instance.Software,
                    Version = actor.Instance.Version,
                    IconUrl = actor.Instance.IconUrl,
                    ThumbnailUrl = actor.Instance.ThumbnailUrl,
                    ContactEmail = actor.Instance.ContactEmail,
                    ContactAccountUsername = actor.Instance.ContactAccountUsername,
                    ActiveUsers = actor.Instance.ActiveUsers,
                    MetadataFetchedAt = actor.Instance.MetadataFetchedAt
                } : null,
                FollowersCount = dto.FollowersCount,
                FollowingCount = dto.FollowingCount,
                LastActivityAt = actor.LastActivityAt,
                LastFetchedAt = actor.LastFetchedAt
            });
        }

        if (cachedActors.Count > 0)
        {
            await cachingService.SetSearchResultsAsync(query, limit, cachedActors);
        }

        return Ok(actorDtos);
    }

    [HttpGet("{id:guid}/posts")]
    [AllowAnonymous]
    public async Task<ActionResult<List<PostResponse>>> GetActorPosts(
        Guid id,
        [FromQuery] int take = 20,
        [FromQuery] int offset = 0
    )
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var postsQuery = db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Actor)
                .ThenInclude(a => a.Instance)
            .Where(p => p.ActorId == id || p.Actor.Uri == actor.Uri)
            .Where(p => p.DraftedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public);

        var boostsQuery = db.Boosts
            .Include(b => b.Post)
                .ThenInclude(p => p.Actor)
                    .ThenInclude(a => a.Instance)
            .Include(b => b.Post)
                .ThenInclude(p => p.Publisher)
            .Where(b => b.ActorId == id)
            .Where(b => b.Post.DraftedAt == null)
            .Where(b => b.Post.Visibility == PostVisibility.Public);

        var totalCount = await postsQuery.CountAsync() + await boostsQuery.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var postsTask = postsQuery.OrderByDescending(p => p.PublishedAt).ToListAsync();
        var boostsTask = boostsQuery.OrderByDescending(b => b.Post.PublishedAt).ToListAsync();

        await Task.WhenAll(postsTask, boostsTask);

        var posts = postsTask.Result;
        var boosts = boostsTask.Result;

        var postResponses = posts.Select(p => new PostResponse
        {
            Id = p.Id,
            Title = p.Title,
            Description = p.Description,
            Slug = p.Slug,
            EditedAt = p.EditedAt,
            DraftedAt = p.DraftedAt,
            PublishedAt = p.PublishedAt,
            Visibility = p.Visibility,
            Content = p.Content,
            ContentType = p.ContentType,
            Type = p.Type,
            PinMode = p.PinMode,
            ActorId = p.ActorId,
            Actor = p.Actor,
            PublisherId = p.PublisherId,
            Publisher = p.Publisher,
            Tags = p.Tags,
            Attachments = p.Attachments,
            BoostInfo = null
        }).ToList();

        var boostResponses = boosts.Select(b => new PostResponse
        {
            Id = b.PostId,
            Title = b.Post.Title,
            Description = b.Post.Description,
            Slug = b.Post.Slug,
            EditedAt = b.Post.EditedAt,
            DraftedAt = b.Post.DraftedAt,
            PublishedAt = b.Post.PublishedAt,
            Visibility = b.Post.Visibility,
            Content = b.Post.Content,
            ContentType = b.Post.ContentType,
            Type = b.Post.Type,
            PinMode = b.Post.PinMode,
            ActorId = b.ActorId,
            Actor = b.Actor,
            PublisherId = b.Post.PublisherId,
            Publisher = b.Post.Publisher,
            Tags = b.Post.Tags,
            Attachments = b.Post.Attachments,
            BoostInfo = new BoostInfo
            {
                BoostId = b.Id,
                BoostedAt = b.BoostedAt,
                ActivityPubUri = b.ActivityPubUri,
                WebUrl = b.WebUrl,
                OriginalPost = b.Post,
                OriginalActor = b.Post.Actor != null ? ToDto(b.Post.Actor) : null
            }
        }).ToList();

        var combined = postResponses.Concat(boostResponses)
            .OrderByDescending(p => p.PublishedAt)
            .Skip(offset)
            .Take(take)
            .ToList();

        return Ok(combined);
    }

    [HttpGet("{id:guid}/followers")]
    [AllowAnonymous]
    public async Task<ActionResult<List<FediverseActorResponse>>> GetActorFollowers(
        Guid id,
        [FromQuery] int take = 40,
        [FromQuery] int offset = 0
    )
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var followerQuery = db.FediverseRelationships
            .Include(r => r.Actor)
                .ThenInclude(a => a.Instance)
            .Where(r => r.TargetActorId == id && r.State == RelationshipState.Accepted)
            .Select(r => r.Actor);

        var totalCount = await followerQuery.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var followers = await followerQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var dtos = new List<FediverseActorResponse>();
        foreach (var follower in followers)
        {
            dtos.Add(await ToDtoAsync(follower));
        }

        return Ok(dtos);
    }

    [HttpGet("{id:guid}/following")]
    [AllowAnonymous]
    public async Task<ActionResult<List<FediverseActorResponse>>> GetActorFollowing(
        Guid id,
        [FromQuery] int take = 40,
        [FromQuery] int offset = 0
    )
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var followingQuery = db.FediverseRelationships
            .Include(r => r.TargetActor)
                .ThenInclude(a => a.Instance)
            .Where(r => r.ActorId == id && r.State == RelationshipState.Accepted)
            .Select(r => r.TargetActor);

        var totalCount = await followingQuery.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var following = await followingQuery
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        var dtos = new List<FediverseActorResponse>();
        foreach (var followed in following)
        {
            dtos.Add(await ToDtoAsync(followed));
        }

        return Ok(dtos);
    }

    [HttpGet("{id:guid}/relationship")]
    [Authorize]
    public async Task<ActionResult<FediverseRelationshipResponse>> GetActorRelationship(Guid id)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        if (currentUser == null)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var userPublishers = await db.Publishers
            .Where(p => p.AccountId == accountId)
            .Select(p => p.Id)
            .ToListAsync();

        var localActorIds = await db.FediverseActors
            .Where(a => a.PublisherId != null && userPublishers.Contains(a.PublisherId.Value))
            .Select(a => a.Id)
            .ToListAsync();

        if (localActorIds.Count == 0)
        {
            return Ok(new FediverseRelationshipResponse
            {
                ActorId = actor.Id,
                ActorUsername = actor.Username,
                ActorInstance = actor.Instance?.Domain,
                ActorHandle = $"{actor.Username}@{actor.Instance?.Domain}",
                IsFollowing = false,
                IsPending = false,
                IsFollowedBy = false
            });
        }

        var localActorId = localActorIds.First();

        var cachedRelationship = await cachingService.GetRelationshipAsync(localActorId, id);
        if (cachedRelationship != null)
        {
            return Ok(CachedRelationshipToResponse(cachedRelationship, actor.Username, actor.Instance?.Domain));
        }

        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r => 
                r.ActorId != null && localActorIds.Contains(r.ActorId) && 
                r.TargetActorId == id);

        var isFollowedBy = await db.FediverseRelationships
            .AnyAsync(r => 
                r.ActorId == id && 
                localActorIds.Contains(r.TargetActorId) &&
                r.State == RelationshipState.Accepted);

        var dto = new FediverseRelationshipResponse
        {
            ActorId = actor.Id,
            ActorUsername = actor.Username,
            ActorInstance = actor.Instance?.Domain,
            ActorHandle = $"{actor.Username}@{actor.Instance?.Domain}",
            IsFollowing = relationship?.State == RelationshipState.Accepted,
            IsPending = relationship?.State == RelationshipState.Pending,
            IsFollowedBy = isFollowedBy
        };

        await cachingService.SetRelationshipAsync(localActorId, id, new CachedRelationship
        {
            ActorId = actor.Id,
            TargetActorId = id,
            IsFollowing = dto.IsFollowing,
            IsPending = dto.IsPending,
            IsFollowedBy = isFollowedBy
        });

        return Ok(dto);
    }

    private async Task<FediverseActorResponse> ToDtoAsync(SnFediverseActor actor)
    {
        var followersCount = await db.FediverseRelationships
            .CountAsync(r => r.TargetActorId == actor.Id && r.State == RelationshipState.Accepted);

        var followingCount = await db.FediverseRelationships
            .CountAsync(r => r.ActorId == actor.Id && r.State == RelationshipState.Accepted);

        var webUrl = actor.Uri;
        if (string.IsNullOrEmpty(webUrl) && !string.IsNullOrEmpty(actor.Username))
        {
            var instance = actor.Instance?.Domain ?? Domain;
            webUrl = $"https://{instance}/@{actor.Username}";
        }

        return new FediverseActorResponse
        {
            Id = actor.Id,
            Type = actor.Type,
            Username = actor.Username,
            FullHandle = $"{actor.Username}@{actor.Instance?.Domain}",
            DisplayName = actor.DisplayName,
            Bio = actor.Bio,
            AvatarUrl = actor.AvatarUrl,
            HeaderUrl = actor.HeaderUrl,
            IsBot = actor.IsBot,
            IsLocked = actor.IsLocked,
            IsDiscoverable = actor.IsDiscoverable,
            InstanceDomain = actor.Instance?.Domain,
            InstanceName = actor.Instance?.Name,
            InstanceSoftware = actor.Instance?.Software,
            Instance = actor.Instance != null ? ToInstanceDto(actor.Instance) : null,
            FollowersCount = followersCount,
            FollowingCount = followingCount,
            LastActivityAt = actor.LastActivityAt,
            LastFetchedAt = actor.LastFetchedAt,
            WebUrl = webUrl
        };
    }

    private static FediverseActorResponse ToDto(SnFediverseActor actor)
    {
        var webUrl = actor.Uri;
        if (string.IsNullOrEmpty(webUrl) && !string.IsNullOrEmpty(actor.Username))
        {
            var instance = actor.Instance?.Domain ?? "localhost";
            webUrl = $"https://{instance}/@{actor.Username}";
        }

        return new FediverseActorResponse
        {
            Id = actor.Id,
            Type = actor.Type,
            Username = actor.Username,
            FullHandle = $"{actor.Username}@{actor.Instance?.Domain}",
            DisplayName = actor.DisplayName,
            Bio = actor.Bio,
            AvatarUrl = actor.AvatarUrl,
            HeaderUrl = actor.HeaderUrl,
            IsBot = actor.IsBot,
            IsLocked = actor.IsLocked,
            IsDiscoverable = actor.IsDiscoverable,
            InstanceDomain = actor.Instance?.Domain,
            InstanceName = actor.Instance?.Name,
            InstanceSoftware = actor.Instance?.Software,
            Instance = actor.Instance != null ? ToInstanceDto(actor.Instance) : null,
            FollowersCount = 0,
            FollowingCount = 0,
            LastActivityAt = actor.LastActivityAt,
            LastFetchedAt = actor.LastFetchedAt,
            WebUrl = webUrl
        };
    }

    private static FediverseInstanceResponse ToInstanceDto(SnFediverseInstance instance)
    {
        return new FediverseInstanceResponse
        {
            Id = instance.Id,
            Domain = instance.Domain,
            Name = instance.Name,
            Description = instance.Description,
            Software = instance.Software,
            Version = instance.Version,
            IconUrl = instance.IconUrl,
            ThumbnailUrl = instance.ThumbnailUrl,
            ContactEmail = instance.ContactEmail,
            ContactAccountUsername = instance.ContactAccountUsername,
            ActiveUsers = instance.ActiveUsers,
            Metadata = instance.Metadata,
            IsBlocked = instance.IsBlocked,
            IsSilenced = instance.IsSilenced,
            LastFetchedAt = instance.LastFetchedAt,
            LastActivityAt = instance.LastActivityAt
        };
    }

    private static FediverseActorResponse CachedActorToResponse(CachedActor cached)
    {
        return new FediverseActorResponse
        {
            Id = cached.Id,
            Type = cached.Type,
            Username = cached.Username,
            FullHandle = cached.FullHandle,
            DisplayName = cached.DisplayName,
            Bio = cached.Bio,
            AvatarUrl = cached.AvatarUrl,
            HeaderUrl = cached.HeaderUrl,
            IsBot = cached.IsBot,
            IsLocked = cached.IsLocked,
            IsDiscoverable = cached.IsDiscoverable,
            InstanceDomain = cached.InstanceDomain,
            InstanceName = cached.InstanceName,
            InstanceSoftware = cached.InstanceSoftware,
            Instance = cached.Instance != null ? CachedInstanceToDto(cached.Instance) : null,
            FollowersCount = cached.FollowersCount,
            FollowingCount = cached.FollowingCount,
            LastActivityAt = cached.LastActivityAt,
            LastFetchedAt = cached.LastFetchedAt,
            WebUrl = cached.WebUrl
        };
    }

    private static FediverseInstanceResponse CachedInstanceToDto(CachedInstance cached)
    {
        return new FediverseInstanceResponse
        {
            Id = cached.Id,
            Domain = cached.Domain,
            Name = cached.Name,
            Description = cached.Description,
            Software = cached.Software,
            Version = cached.Version,
            IconUrl = cached.IconUrl,
            ThumbnailUrl = cached.ThumbnailUrl,
            ContactEmail = cached.ContactEmail,
            ContactAccountUsername = cached.ContactAccountUsername,
            ActiveUsers = cached.ActiveUsers,
            Metadata = null,
            IsBlocked = false,
            IsSilenced = false,
            LastFetchedAt = cached.MetadataFetchedAt,
            LastActivityAt = null
        };
    }

    private static FediverseRelationshipResponse CachedRelationshipToResponse(
        CachedRelationship cached, string actorUsername, string? actorInstance)
    {
        return new FediverseRelationshipResponse
        {
            ActorId = cached.ActorId,
            ActorUsername = actorUsername,
            ActorInstance = actorInstance,
            ActorHandle = $"{actorUsername}@{actorInstance}",
            IsFollowing = cached.IsFollowing,
            IsFollowedBy = cached.IsFollowedBy,
            IsPending = cached.IsPending
        };
    }

    private async Task<List<SnPost>> GetActorPostsInternalAsync(Guid actorId, int limit)
    {
        return await db.Posts
            .Include(p => p.Publisher)
            .Include(p => p.Actor)
            .Where(p => p.ActorId == actorId)
            .Where(p => p.DraftedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .OrderByDescending(p => p.PublishedAt)
            .Take(limit)
            .ToListAsync();
    }
}

public class FediverseActorResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "Person";
    public string Username { get; set; } = null!;
    public string FullHandle { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public string? AvatarUrl { get; set; }
    public string? HeaderUrl { get; set; }
    public bool IsBot { get; set; }
    public bool IsLocked { get; set; }
    public bool IsDiscoverable { get; set; } = true;
    public string? InstanceDomain { get; set; }
    public string? InstanceName { get; set; }
    public string? InstanceSoftware { get; set; }
    public FediverseInstanceResponse? Instance { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public Instant? LastActivityAt { get; set; }
    public Instant? LastFetchedAt { get; set; }
    public string WebUrl { get; set; } = null!;
    public List<SnPost>? RecentPosts { get; set; }
}

public class FediverseInstanceResponse
{
    public Guid? Id { get; set; }
    public string Domain { get; set; } = null!;
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Software { get; set; }
    public string? Version { get; set; }
    public string? IconUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactAccountUsername { get; set; }
    public int? ActiveUsers { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsSilenced { get; set; }
    public Instant? LastFetchedAt { get; set; }
    public Instant? LastActivityAt { get; set; }
}

public class FediverseRelationshipResponse
{
    public Guid ActorId { get; set; }
    public string ActorUsername { get; set; } = null!;
    public string? ActorInstance { get; set; }
    public string ActorHandle { get; set; } = null!;
    public bool IsFollowing { get; set; }
    public bool IsFollowedBy { get; set; }
    public bool IsPending { get; set; }
}
