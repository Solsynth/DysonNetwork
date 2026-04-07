using System.Text.Json;
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
    ActivityPubDeliveryService deliveryService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<FediverseActorController> logger
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpGet("{username}@{instance}")]
    [AllowAnonymous]
    public async Task<ActionResult<SnFediverseActor>> GetActorByHandle(
        string username,
        string instance,
        [FromQuery] bool includeActivity = false
    )
    {
        var cachedActor = await cachingService.GetActorByHandleAsync(username, instance);
        if (cachedActor != null)
        {
            var response = CachedActorToEntity(cachedActor);
            if (includeActivity)
            {
                response.RecentPosts = await GetActorPostsInternalAsync(cachedActor.Id, 5);
            }
            return Ok(response);
        }

        var dbActor = await cachingService.GetActorFromDbByHandleAsync(username, instance);
        if (dbActor == null)
        {
            var discoveredActor = await discoveryService.DiscoverActorAsync(
                $"{username}@{instance}"
            );
            if (discoveredActor == null)
                return NotFound(new { error = "Actor not found" });

            await cachingService.SetActorAsync(discoveredActor, instance);
            dbActor = await cachingService.GetActorByHandleAsync(username, instance);
            if (dbActor == null)
                return NotFound(new { error = "Actor not found" });
        }

        var dto = CachedActorToEntity(dbActor);

        if (includeActivity)
        {
            dto.RecentPosts = await GetActorPostsInternalAsync(dbActor.Id, 5);
        }

        return Ok(dto);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<SnFediverseActor>> GetActorById(
        Guid id,
        [FromQuery] bool includeActivity = false
    )
    {
        var cachedActor = await cachingService.GetActorByIdAsync(id);
        if (cachedActor != null)
        {
            var response = CachedActorToEntity(cachedActor);
            if (includeActivity)
            {
                response.RecentPosts = await GetActorPostsInternalAsync(cachedActor.Id, 5);
            }
            return Ok(response);
        }

        var actor = await cachingService.GetActorFromDbByIdAsync(id);
        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var dto = CachedActorToEntity(actor);

        if (includeActivity)
        {
            dto.RecentPosts = await GetActorPostsInternalAsync(actor.Id, 5);
        }

        return Ok(dto);
    }

    [HttpGet("search")]
    [AllowAnonymous]
    public async Task<ActionResult<List<SnFediverseActor>>> SearchActors(
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
            return Ok(cachedResults.Select(CachedActorToEntity).ToList());
        }

        var remoteActors = await discoveryService.SearchActorsAsync(
            query,
            limit,
            includeRemoteDiscovery: true
        );

        var actorList = new List<SnFediverseActor>();
        var cachedActors = new List<CachedActor>();

        // Get post counts for all actors from local DB
        var actorIds = remoteActors.Select(a => a.Id).ToList();
        var actorUris = remoteActors.Where(a => a.Uri != null).Select(a => a.Uri!).ToList();

        // Get post counts by ActorId
        var postCountList = await db.Posts
            .Where(p => p.ActorId != null && actorIds.Contains(p.ActorId.Value))
            .GroupBy(p => p.ActorId!.Value)
            .Select(g => new { ActorId = g.Key, Count = g.Count() })
            .ToListAsync();

        // Get post counts by Actor Uri (for actors where posts use Uri instead of ID)
        var postsByUri = await db.Posts
            .Include(p => p.Actor)
            .Where(p => p.Actor != null && actorUris.Contains(p.Actor.Uri))
            .GroupBy(p => p.Actor!.Uri!)
            .Select(g => new { ActorUri = g.Key, Count = g.Count() })
            .ToListAsync();

        var postCountDict = new Dictionary<Guid, int>();
        foreach (var item in postCountList)
        {
            postCountDict[item.ActorId] = item.Count;
        }
        foreach (var item in postsByUri)
        {
            var actor = remoteActors.FirstOrDefault(a => a.Uri == item.ActorUri);
            if (actor != null)
            {
                postCountDict[actor.Id] = postCountDict.GetValueOrDefault(actor.Id, 0) + item.Count;
            }
        }

        foreach (var actor in remoteActors)
        {
            var localPostCount = postCountDict.GetValueOrDefault(actor.Id, 0);
            // Use remote total if available, otherwise use local count
            actor.PostCount = actor.TotalPostCount ?? localPostCount;

            actorList.Add(actor);

            cachedActors.Add(
                new CachedActor
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
                    Instance =
                        actor.Instance != null
                            ? new CachedInstance
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
                                MetadataFetchedAt = actor.Instance.MetadataFetchedAt,
                            }
                            : null,
                    FollowersCount = actor.FollowersCount,
                    FollowingCount = actor.FollowingCount,
                    PostCount = actor.PostCount,
                    TotalPostCount = actor.TotalPostCount,
                    LastActivityAt = actor.LastActivityAt,
                    LastFetchedAt = actor.LastFetchedAt,
                }
            );
        }

        if (cachedActors.Count > 0)
        {
            await cachingService.SetSearchResultsAsync(query, limit, cachedActors);
        }

        return Ok(actorList);
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

        var posts = await postsQuery.OrderByDescending(p => p.PublishedAt).ToListAsync();
        var boosts = await boostsQuery.OrderByDescending(b => b.Post.PublishedAt).ToListAsync();
        var remotePosts = await FetchRemoteOutboxPostsAsync(actor, take * 2);

        // Build set of URIs to exclude (both FediverseUri and ActivityPubUri for local posts/boosts)
        var localUris = new HashSet<string>();

        // Include all local post URIs (both FediverseUri and direct ID matching)
        foreach (var p in posts)
        {
            if (!string.IsNullOrEmpty(p.FediverseUri))
                localUris.Add(p.FediverseUri);
            // Also add by ID for posts that might be referenced differently
            localUris.Add(p.Id.ToString());
        }

        // Include all boost URIs
        foreach (var b in boosts)
        {
            if (!string.IsNullOrEmpty(b.ActivityPubUri))
                localUris.Add(b.ActivityPubUri);
            if (!string.IsNullOrEmpty(b.Post.FediverseUri))
                localUris.Add(b.Post.FediverseUri);
            localUris.Add(b.PostId.ToString());
        }

        // Also add remote post URIs we've already seen to prevent duplicates within remote list
        var seenRemoteUris = new HashSet<string>();

        var localPostResponses = posts
            .Select(p => new PostResponse
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
                BoostInfo = null,
                IsCached = true,
            })
            .ToList();

        var boostResponses = boosts
            .Select(b => new PostResponse
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
                    OriginalActor = b.Post.Actor,
                },
                IsCached = true,
            })
            .ToList();

        // Filter remote posts: exclude those already in local DB and deduplicate within remote list
        var remotePostResponses = new List<PostResponse>();
        foreach (var r in remotePosts)
        {
            var uri = r.FediverseUri ?? r.WebUrl ?? "";
            if (string.IsNullOrEmpty(uri) || localUris.Contains(uri) || seenRemoteUris.Contains(uri))
                continue;

            seenRemoteUris.Add(uri);
            var postResponse = r.ToPostResponse(actor);
            remotePostResponses.Add(postResponse);
        }

        var totalCount = localPostResponses.Count + boostResponses.Count + remotePostResponses.Count;
        Response.Headers["X-Total"] = totalCount.ToString();

        var combined = localPostResponses
            .Concat(boostResponses)
            .Concat(remotePostResponses)
            .OrderByDescending(p => p.PublishedAt)
            .Skip(offset)
            .Take(take)
            .ToList();

        return Ok(combined);
    }

    private async Task<List<RemotePost>> FetchRemoteOutboxPostsAsync(
        SnFediverseActor actor,
        int limit
    )
    {
        if (string.IsNullOrEmpty(actor.OutboxUri))
            return [];

        try
        {
            var client = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, actor.OutboxUri);
            request.Headers.Accept.ParseAdd("application/activity+json");
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug(
                    "Failed to fetch outbox from {Url}: {Status}",
                    actor.OutboxUri,
                    response.StatusCode
                );
                return [];
            }

            var json = await response.Content.ReadAsStringAsync();
            var outboxData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            if (outboxData == null)
                return [];

            var orderedItems =
                outboxData.GetValueOrDefault("orderedItems") as JsonElement?
                ?? (
                    outboxData.GetValueOrDefault("first") is JsonElement firstPageElement
                        ? (
                            firstPageElement.ValueKind == JsonValueKind.String
                            && firstPageElement.GetString()?.StartsWith("http") == true
                                ? await FetchOutboxPageAsync(firstPageElement.GetString()!)
                                : null
                        )
                        : null
                ) as JsonElement?;

            if (orderedItems == null)
                return [];

            var items = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(
                orderedItems.Value.GetRawText()
            );
            if (items == null)
                return [];

            var remotePosts = new List<RemotePost>();
            foreach (var item in items.Take(limit))
            {
                var activityType = item.GetValueOrDefault("type")?.ToString();
                var activityId = item.GetValueOrDefault("id")?.ToString();

                if (activityType == "Create")
                {
                    var obj = item.GetValueOrDefault("object");
                    var postDict = ConvertToDictionary(obj);
                    if (postDict != null)
                    {
                        var postType = postDict.GetValueOrDefault("type")?.ToString();
                        if (postType == "Note" || postType == "Article")
                        {
                            remotePosts.Add(
                                RemotePost.FromActivityStream(postDict, activityId, actor)
                            );
                        }
                    }
                }
                else if (activityType == "Announce")
                {
                    var obj = item.GetValueOrDefault("object");
                    var postDict = ConvertToDictionary(obj);
                    if (postDict != null)
                    {
                        var postType = postDict.GetValueOrDefault("type")?.ToString();
                        if (postType == "Note" || postType == "Article")
                        {
                            var boostedAt = ParseInstant(item.GetValueOrDefault("published"));
                            remotePosts.Add(
                                RemotePost.FromAnnounce(postDict, activityId, actor, boostedAt)
                            );
                        }
                    }
                }
            }

            return remotePosts;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch remote outbox for actor {ActorUri}", actor.Uri);
            return [];
        }
    }

    private async Task<JsonElement?> FetchOutboxPageAsync(string pageUrl)
    {
        try
        {
            var client = httpClientFactory.CreateClient();
            var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            request.Headers.Accept.ParseAdd("application/activity+json");
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            var pageData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return pageData?.GetValueOrDefault("orderedItems") as JsonElement?;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object>? ConvertToDictionary(object? obj)
    {
        return obj switch
        {
            null => null,
            Dictionary<string, object> dict => dict,
            JsonElement element when element.ValueKind == JsonValueKind.Object =>
                JsonSerializer.Deserialize<Dictionary<string, object>>(element.GetRawText()),
            _ => null,
        };
    }

    private static Instant? ParseInstant(object? value)
    {
        if (value == null)
            return null;

        try
        {
            string? str = null;
            if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
            {
                str = element.GetString();
            }
            else if (value is string s)
            {
                str = s;
            }

            if (!string.IsNullOrEmpty(str) && DateTimeOffset.TryParse(str, out var dto))
            {
                return Instant.FromDateTimeOffset(dto);
            }
        }
        catch { }

        return null;
    }

    private class RemotePost
    {
        public string? FediverseUri { get; set; }
        public string? WebUrl { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Content { get; set; }
        public Instant? PublishedAt { get; set; }
        public Instant? EditedAt { get; set; }
        public Instant? BoostedAt { get; set; }
        public string? ActorUsername { get; set; }
        public string? ActorDisplayName { get; set; }
        public string? ActorUri { get; set; }
        public string? ActorAvatarUrl { get; set; }
        public bool IsBoost { get; set; }
        public string? OriginalActorUsername { get; set; }
        public string? OriginalActorDisplayName { get; set; }
        public string? OriginalActorUri { get; set; }
        public string? OriginalActorAvatarUrl { get; set; }
        public List<SnCloudFileReferenceObject>? Attachments { get; set; }

        public static RemotePost FromActivityStream(
            Dictionary<string, object> obj,
            string? activityId,
            SnFediverseActor actor
        )
        {
            var published = obj.GetValueOrDefault("published");
            var id = obj.GetValueOrDefault("id")?.ToString();
            var content = obj.GetValueOrDefault("content")?.ToString();
            var name =
                obj.GetValueOrDefault("name")?.ToString()
                ?? obj.GetValueOrDefault("summary")?.ToString();
            var summary = obj.GetValueOrDefault("summary")?.ToString();

            var attributedTo = obj.GetValueOrDefault("attributedTo");
            string? actorUsername = null;
            string? actorUri = null;
            if (attributedTo != null)
            {
                if (attributedTo is JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.String)
                        actorUri = element.GetString();
                    else if (element.ValueKind == JsonValueKind.Object)
                    {
                        actorUsername = element.TryGetProperty("preferredUsername", out var u)
                            ? u.GetString()
                            : null;
                        actorUri = element.TryGetProperty("id", out var idProp)
                            ? idProp.GetString()
                            : null;
                    }
                }
                else if (attributedTo is Dictionary<string, object> attrDict)
                {
                    actorUsername = attrDict.GetValueOrDefault("preferredUsername")?.ToString();
                    actorUri = attrDict.GetValueOrDefault("id")?.ToString();
                }
            }

            var webUrl = obj.GetValueOrDefault("url");
            string? webUrlStr = null;
            if (webUrl != null)
            {
                if (webUrl is JsonElement urlElement)
                    webUrlStr = urlElement.GetString();
                else
                    webUrlStr = webUrl.ToString();
            }

            // Parse attachments
            var attachments = ParseAttachments(obj.GetValueOrDefault("attachment"));

            return new RemotePost
            {
                FediverseUri = id ?? activityId,
                WebUrl = webUrlStr,
                Title = name,
                Description = summary,
                Content = content,
                PublishedAt = ParseInstantValue(published),
                ActorUsername = actorUsername ?? actor.Username,
                ActorDisplayName = actor.DisplayName,
                ActorUri = actorUri ?? actor.Uri,
                ActorAvatarUrl = actor.AvatarUrl,
                IsBoost = false,
                Attachments = attachments,
            };
        }

        public static RemotePost FromAnnounce(
            Dictionary<string, object> obj,
            string? activityId,
            SnFediverseActor actor,
            Instant? boostedAt
        )
        {
            var original = FromActivityStream(obj, activityId, actor);
            original.IsBoost = true;
            original.BoostedAt = boostedAt;
            original.OriginalActorUsername = original.ActorUsername;
            original.OriginalActorDisplayName = original.ActorDisplayName;
            original.OriginalActorUri = original.ActorUri;
            original.OriginalActorAvatarUrl = original.ActorAvatarUrl;
            original.ActorUsername = actor.Username;
            original.ActorDisplayName = actor.DisplayName;
            original.ActorUri = actor.Uri;
            original.ActorAvatarUrl = actor.AvatarUrl;
            return original;
        }

        public PostResponse ToPostResponse(SnFediverseActor actor)
        {
            var domain = ExtractDomain(OriginalActorUri ?? ActorUri ?? actor.Uri);
            var originalActor = new SnFediverseActor
            {
                Id = Guid.Empty,
                Username = OriginalActorUsername ?? ActorUsername ?? actor.Username,
                DisplayName = OriginalActorDisplayName ?? ActorDisplayName ?? actor.DisplayName,
                AvatarUrl = OriginalActorAvatarUrl ?? ActorAvatarUrl ?? actor.AvatarUrl,
                Uri = OriginalActorUri ?? ActorUri ?? actor.Uri ?? "",
                Instance = new SnFediverseInstance { Domain = domain ?? "unknown" },
            };

            var originalDomain = ExtractDomain(OriginalActorUri ?? ActorUri ?? actor.Uri);
            var originalInstance = new SnFediverseInstance { Domain = originalDomain ?? "unknown" };
            originalActor.Instance = originalInstance;

            if (IsBoost)
            {
                // For boosts, return the original post data with boost info
                return new PostResponse
                {
                    Id = Guid.Empty,
                    FediverseUri = FediverseUri,
                    Title = Title,
                    Description = Description,
                    Content = Content,
                    ContentType = Content?.Contains("<") == true ? PostContentType.Html : PostContentType.Markdown,
                    Type = PostType.Moment,
                    PublishedAt = PublishedAt,
                    Visibility = PostVisibility.Public,
                    ActorId = actor.Id,
                    Actor = actor,
                    PinMode = null,
                    Attachments = Attachments ?? [],
                    BoostInfo = new BoostInfo
                    {
                        BoostId = Guid.Empty,
                        BoostedAt = BoostedAt ?? PublishedAt ?? Instant.MinValue,
                        ActivityPubUri = FediverseUri,
                        WebUrl = WebUrl,
                        OriginalPost = new SnPost
                        {
                            FediverseUri = FediverseUri,
                            Content = Content,
                            Title = Title,
                            Description = Description,
                            PublishedAt = PublishedAt,
                        },
                        OriginalActor = originalActor,
                    },
                    IsCached = false,
                };
            }
            else
            {
                // For regular posts, return as the post by the original author
                return new PostResponse
                {
                    Id = Guid.Empty,
                    FediverseUri = FediverseUri,
                    Title = Title,
                    Description = Description,
                    Content = Content,
                    ContentType = Content?.Contains("<") == true ? PostContentType.Html : PostContentType.Markdown,
                    Type = PostType.Moment,
                    PublishedAt = PublishedAt,
                    Visibility = PostVisibility.Public,
                    ActorId = Guid.Empty,
                    Actor = originalActor,
                    PinMode = null,
                    Attachments = Attachments ?? [],
                    BoostInfo = null,
                    IsCached = false,
                };
            }
        }

        private static string? ExtractDomain(string? uri)
        {
            if (string.IsNullOrEmpty(uri))
                return null;
            try
            {
                return new Uri(uri).Host;
            }
            catch
            {
                return null;
            }
        }

        private static List<SnCloudFileReferenceObject>? ParseAttachments(object? value)
        {
            if (value == null)
                return null;

            var attachments = value switch
            {
                JsonElement { ValueKind: JsonValueKind.Array } element
                    => element.EnumerateArray().Select(e => ConvertJsonElementToDict(e)).ToList(),
                List<Dictionary<string, object>> list => list,
                _ => null
            };

            return attachments?.Select(dict => new SnCloudFileReferenceObject
            {
                Id = Guid.NewGuid().ToString(),
                Name = dict.GetValueOrDefault("name")?.ToString() ?? string.Empty,
                Url = dict.GetValueOrDefault("url")?.ToString(),
                MimeType = dict.GetValueOrDefault("mediaType")?.ToString(),
                Width = TryGetIntFromDict(dict, "width"),
                Height = TryGetIntFromDict(dict, "height"),
                Blurhash = dict.GetValueOrDefault("blurhash")?.ToString(),
                FileMeta = new Dictionary<string, object?>(),
                UserMeta = new Dictionary<string, object?>(),
                Size = 0,
                CreatedAt = Instant.FromDateTimeOffset(DateTimeOffset.UtcNow),
                UpdatedAt = Instant.FromDateTimeOffset(DateTimeOffset.UtcNow)
            }).ToList();
        }

        private static Dictionary<string, object> ConvertJsonElementToDict(JsonElement element)
        {
            var dict = new Dictionary<string, object>();
            foreach (var prop in element.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString() ?? "",
                    JsonValueKind.Number => prop.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null!,
                    JsonValueKind.Object => ConvertJsonElementToDict(prop.Value),
                    JsonValueKind.Array => prop.Value.EnumerateArray()
                        .Select(ConvertJsonElementToDict).ToList(),
                    _ => prop.Value.ToString()
                };
            }
            return dict;
        }

        private static int? TryGetIntFromDict(Dictionary<string, object> dict, string key)
        {
            var value = dict.GetValueOrDefault(key);
            if (value == null) return null;
            if (value is JsonElement element && element.ValueKind == JsonValueKind.Number)
                return element.GetInt32();
            if (value is int i) return i;
            if (value is double d) return (int)d;
            return null;
        }

        private static Instant? ParseInstantValue(object? value)
        {
            if (value == null)
                return null;
            try
            {
                string? str = null;
                if (value is JsonElement element && element.ValueKind == JsonValueKind.String)
                    str = element.GetString();
                else if (value is string s)
                    str = s;

                if (!string.IsNullOrEmpty(str) && DateTimeOffset.TryParse(str, out var dto))
                    return Instant.FromDateTimeOffset(dto);
            }
            catch { }
            return null;
        }
    }

    [HttpGet("{id:guid}/followers")]
    [AllowAnonymous]
    public async Task<ActionResult<List<SnFediverseActor>>> GetActorFollowers(
        Guid id,
        [FromQuery] int take = 40,
        [FromQuery] int offset = 0
    )
    {
        var actor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var followerQuery = db
            .FediverseRelationships.Include(r => r.Actor)
            .ThenInclude(a => a.Instance)
            .Where(r => r.TargetActorId == id && r.State == RelationshipState.Accepted)
            .Select(r => r.Actor);

        var totalCount = await followerQuery.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var followers = await followerQuery.Skip(offset).Take(take).ToListAsync();

        return Ok(followers);
    }

    [HttpGet("{id:guid}/following")]
    [AllowAnonymous]
    public async Task<ActionResult<List<SnFediverseActor>>> GetActorFollowing(
        Guid id,
        [FromQuery] int take = 40,
        [FromQuery] int offset = 0
    )
    {
        var actor = await db.FediverseActors.FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var followingQuery = db
            .FediverseRelationships.Include(r => r.TargetActor)
            .ThenInclude(a => a.Instance)
            .Where(r => r.ActorId == id && r.State == RelationshipState.Accepted)
            .Select(r => r.TargetActor);

        var totalCount = await followingQuery.CountAsync();
        Response.Headers["X-Total"] = totalCount.ToString();

        var following = await followingQuery.Skip(offset).Take(take).ToListAsync();

        return Ok(following);
    }

    [HttpGet("{id:guid}/relationship")]
    [Authorize]
    public async Task<ActionResult<FediverseRelationshipResponse>> GetActorRelationship(Guid id)
    {
        var currentUser = HttpContext.Items["CurrentUser"] as DyAccount;
        if (currentUser == null)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var actor = await db
            .FediverseActors.Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (actor == null)
            return NotFound(new { error = "Actor not found" });

        var userPublishers = await db
            .Publishers.Where(p => p.AccountId == accountId)
            .Select(p => p.Id)
            .ToListAsync();

        var localActorIds = await db
            .FediverseActors.Where(a =>
                a.PublisherId != null && userPublishers.Contains(a.PublisherId.Value)
            )
            .Select(a => a.Id)
            .ToListAsync();

        if (localActorIds.Count == 0)
        {
            return Ok(
                new FediverseRelationshipResponse
                {
                    ActorId = actor.Id,
                    ActorUsername = actor.Username,
                    ActorInstance = actor.Instance?.Domain,
                    ActorHandle = $"{actor.Username}@{actor.Instance?.Domain}",
                    IsFollowing = false,
                    IsPending = false,
                    IsFollowedBy = false,
                }
            );
        }

        var localActorId = localActorIds.First();

        var cachedRelationship = await cachingService.GetRelationshipAsync(localActorId, id);
        if (cachedRelationship != null)
        {
            return Ok(
                CachedRelationshipToResponse(
                    cachedRelationship,
                    actor.Username,
                    actor.Instance?.Domain
                )
            );
        }

        var relationship = await db.FediverseRelationships.FirstOrDefaultAsync(r =>
            r.ActorId != null && localActorIds.Contains(r.ActorId) && r.TargetActorId == id
        );

        var isFollowedBy = await db.FediverseRelationships.AnyAsync(r =>
            r.ActorId == id
            && localActorIds.Contains(r.TargetActorId)
            && r.State == RelationshipState.Accepted
        );

        var dto = new FediverseRelationshipResponse
        {
            ActorId = actor.Id,
            ActorUsername = actor.Username,
            ActorInstance = actor.Instance?.Domain,
            ActorHandle = $"{actor.Username}@{actor.Instance?.Domain}",
            IsFollowing = relationship?.State == RelationshipState.Accepted,
            IsPending = relationship?.State == RelationshipState.Pending,
            IsFollowedBy = isFollowedBy,
        };

        await cachingService.SetRelationshipAsync(
            localActorId,
            id,
            new CachedRelationship
            {
                ActorId = actor.Id,
                TargetActorId = id,
                IsFollowing = dto.IsFollowing,
                IsPending = dto.IsPending,
                IsFollowedBy = isFollowedBy,
            }
        );

        return Ok(dto);
    }

    private static SnFediverseActor CachedActorToEntity(CachedActor cached)
    {
        var instance =
            cached.Instance != null
                ? new SnFediverseInstance
                {
                    Id = cached.Instance.Id,
                    Domain = cached.Instance.Domain,
                    Name = cached.Instance.Name,
                    Description = cached.Instance.Description,
                    Software = cached.Instance.Software,
                    Version = cached.Instance.Version,
                    IconUrl = cached.Instance.IconUrl,
                    ThumbnailUrl = cached.Instance.ThumbnailUrl,
                    ContactEmail = cached.Instance.ContactEmail,
                    ContactAccountUsername = cached.Instance.ContactAccountUsername,
                    ActiveUsers = cached.Instance.ActiveUsers,
                    MetadataFetchedAt = cached.Instance.MetadataFetchedAt,
                }
                : null;

        return new SnFediverseActor
        {
            Id = cached.Id,
            Type = cached.Type,
            Uri = cached.Uri,
            Username = cached.Username,
            DisplayName = cached.DisplayName,
            Bio = cached.Bio,
            AvatarUrl = cached.AvatarUrl,
            HeaderUrl = cached.HeaderUrl,
            IsBot = cached.IsBot,
            IsLocked = cached.IsLocked,
            IsDiscoverable = cached.IsDiscoverable,
            InstanceId = instance?.Id ?? Guid.Empty,
            Instance =
                instance
                ?? new SnFediverseInstance { Domain = cached.InstanceDomain ?? "localhost" },
            FollowersCount = cached.FollowersCount,
            FollowingCount = cached.FollowingCount,
            PostCount = cached.PostCount,
            TotalPostCount = cached.TotalPostCount,
            LastActivityAt = cached.LastActivityAt,
            LastFetchedAt = cached.LastFetchedAt,
            Metadata = cached.Metadata,
        };
    }

    private static FediverseRelationshipResponse CachedRelationshipToResponse(
        CachedRelationship cached,
        string actorUsername,
        string? actorInstance
    )
    {
        return new FediverseRelationshipResponse
        {
            ActorId = cached.ActorId,
            ActorUsername = actorUsername,
            ActorInstance = actorInstance,
            ActorHandle = $"{actorUsername}@{actorInstance}",
            IsFollowing = cached.IsFollowing,
            IsFollowedBy = cached.IsFollowedBy,
            IsPending = cached.IsPending,
        };
    }

    private async Task<List<SnPost>> GetActorPostsInternalAsync(Guid actorId, int limit)
    {
        return await db
            .Posts.Include(p => p.Publisher)
            .Include(p => p.Actor)
            .Where(p => p.ActorId == actorId)
            .Where(p => p.DraftedAt == null)
            .Where(p => p.Visibility == PostVisibility.Public)
            .OrderByDescending(p => p.PublishedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Check if the current user has any publishers with Fediverse enabled.
    /// </summary>
    [HttpGet("availability")]
    [Authorize]
    public async Task<ActionResult<FediverseAvailabilityResponse>> CheckAvailability()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        // Get all publishers owned by the user
        var ownedPublishers = await db.Publishers
            .Where(p => p.AccountId == accountId)
            .ToListAsync();

        if (ownedPublishers.Count == 0)
        {
            return Ok(new FediverseAvailabilityResponse
            {
                IsEnabled = false,
                Publishers = []
            });
        }

        var publisherIds = ownedPublishers.Select(p => p.Id).ToList();

        // Check which publishers have Fediverse actors
        var fediverseActors = await db.FediverseActors
            .Include(a => a.Instance)
            .Where(a => a.PublisherId != null && publisherIds.Contains(a.PublisherId.Value))
            .ToListAsync();

        // Only fetch stats for REMOTE actors (not local ones)
        // Local actors have stats in our DB already
        var localDomain = Domain;
        var remoteActorsNeedingStats = fediverseActors
            .Where(a => a.Instance?.Domain != localDomain)
            .Where(a => a.FollowersCount == 0 || a.FollowingCount == 0 || a.TotalPostCount == null)
            .ToList();

        if (remoteActorsNeedingStats.Count > 0)
        {
            var statsTasks = remoteActorsNeedingStats
                .Select(a => discoveryService.FetchActorStatsAsync(a))
                .ToList();
            await Task.WhenAll(statsTasks);
        }

        var enabledPublishers = fediverseActors.Select(a => new FediversePublisherInfo
        {
            PublisherId = a.PublisherId!.Value,
            PublisherName = ownedPublishers.FirstOrDefault(p => p.Id == a.PublisherId)?.Name ?? "Unknown",
            FediverseHandle = $"{a.Username}@{a.Instance?.Domain}",
            FediverseUri = a.Uri,
            AvatarUrl = a.AvatarUrl,
            IsEnabled = true,
            FollowersCount = a.FollowersCount,
            FollowingCount = a.FollowingCount,
            PostsCount = a.TotalPostCount ?? 0
        }).ToList();

        return Ok(new FediverseAvailabilityResponse
        {
            IsEnabled = enabledPublishers.Count > 0,
            Publishers = enabledPublishers
        });
    }

    [HttpPost("{id:guid}/follow")]
    [Authorize]
    public async Task<ActionResult> FollowActor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == accountId))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return BadRequest(new { error = "User doesn't have a publisher" });

        var targetActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Id == id);

        if (targetActor == null)
            return NotFound(new { error = "Actor not found" });

        var success = await deliveryService.SendFollowActivityAsync(
            publisher.Id,
            targetActor.Uri
        );

        if (success)
        {
            return Ok(new
            {
                success = true,
                message = "Follow request sent. Waiting for acceptance.",
                targetActorUri = targetActor.Uri
            });
        }

        return BadRequest(new { error = "Failed to send follow request" });
    }

    [HttpPost("{id:guid}/unfollow")]
    [Authorize]
    public async Task<ActionResult> UnfollowActor(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == accountId))
            .FirstOrDefaultAsync();

        if (publisher == null)
            return BadRequest(new { error = "User doesn't have a publisher" });

        var targetActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Id == id);

        if (targetActor == null)
            return NotFound(new { error = "Actor not found" });

        var success = await deliveryService.SendUnfollowActivityAsync(
            publisher.Id,
            targetActor.Uri
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

public class FediverseAvailabilityResponse
{
    public bool IsEnabled { get; set; }
    public List<FediversePublisherInfo> Publishers { get; set; } = [];
}

public class FediversePublisherInfo
{
    public Guid PublisherId { get; set; }
    public string PublisherName { get; set; } = null!;
    public string FediverseHandle { get; set; } = null!;
    public string FediverseUri { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public bool IsEnabled { get; set; }
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public int PostsCount { get; set; }
}
