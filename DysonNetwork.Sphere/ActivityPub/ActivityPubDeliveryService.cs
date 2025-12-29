using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubDeliveryService(
    AppDatabase db,
    ActivityPubSignatureService signatureService,
    ActivityPubDiscoveryService discoveryService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ActivityPubDeliveryService> logger
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private HttpClient HttpClient => httpClientFactory.CreateClient();

    public async Task<bool> SendAcceptActivityAsync(
        Guid publisherId,
        string followerActorUri,
        string followActivityId
    )
    {
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return false;
        
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var followerActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == followerActorUri);
        
        if (followerActor?.InboxUri == null)
        {
            logger.LogWarning("Follower actor or inbox not found: {Uri}", followerActorUri);
            return false;
        }
        
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/accepts/{Guid.NewGuid()}",
            ["type"] = "Accept",
            ["actor"] = actorUrl,
            ["object"] = followActivityId
        };
        
        return await SendActivityToInboxAsync(activity, followerActor.InboxUri, actorUrl);
    }

    public async Task<bool> SendFollowActivityAsync(
        Guid publisherId,
        string targetActorUri
    )
    {
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return false;
        
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var targetActor = await GetOrFetchActorAsync(targetActorUri);
        var localActor = await GetOrCreateLocalActorAsync(publisher);
        
        if (targetActor?.InboxUri == null || localActor == null)
        {
            logger.LogWarning("Target actor or inbox not found: {Uri}", targetActorUri);
            return false;
        }
        
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/follows/{Guid.NewGuid()}",
            ["type"] = "Follow",
            ["actor"] = actorUrl,
            ["object"] = targetActorUri
        };
        
        await db.FediverseRelationships.AddAsync(new SnFediverseRelationship
        {
            IsLocalActor = true,
            LocalPublisherId = publisher.Id,
            ActorId = localActor.Id,
            TargetActorId = targetActor.Id,
            State = RelationshipState.Pending,
            IsFollowing = true,
            IsFollowedBy = false
        });
        
        await db.SaveChangesAsync();
        
        return await SendActivityToInboxAsync(activity, targetActor.InboxUri, actorUrl);
    }

    public async Task<bool> SendCreateActivityAsync(SnPost post)
    {
        var publisher = await db.Publishers.FindAsync(post.PublisherId);
        if (publisher == null)
            return false;
        
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var postUrl = $"https://{Domain}/posts/{post.Id}";
        
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{postUrl}/activity",
            ["type"] = "Create",
            ["actor"] = actorUrl,
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = new Dictionary<string, object>
            {
                ["id"] = postUrl,
                ["type"] = post.Type == PostType.Article ? "Article" : "Note",
                ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
                ["attributedTo"] = actorUrl,
                ["content"] = post.Content ?? "",
                ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
                ["cc"] = new[] { $"{actorUrl}/followers" },
                ["attachment"] = post.Attachments.Select(a => new Dictionary<string, object>
                {
                    ["type"] = "Document",
                    ["mediaType"] = "image/jpeg",
                    ["url"] = $"https://{Domain}/api/files/{a.Id}"
                }).ToList<object>()
            }
        };
        
        var followers = await GetRemoteFollowersAsync(publisher.Id);
        var successCount = 0;
        
        foreach (var follower in followers)
        {
            if (follower.InboxUri != null)
            {
                var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
                if (success)
                    successCount++;
            }
        }
        
        logger.LogInformation("Sent Create activity to {Count}/{Total} followers", 
            successCount, followers.Count);
        
        return successCount > 0;
    }

    public async Task<bool> SendLikeActivityAsync(
        Guid postId,
        Guid accountId,
        string targetActorUri
    )
    {
        var publisher = await db.Publishers
            .Include(p => p.Members)
            .Where(p => p.Members.Any(m => m.AccountId == accountId))
            .FirstOrDefaultAsync();
        
        if (publisher == null)
            return false;
        
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var postUrl = $"https://{Domain}/posts/{postId}";
        var targetActor = await GetOrFetchActorAsync(targetActorUri);
        
        if (targetActor?.InboxUri == null)
            return false;
        
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/likes/{Guid.NewGuid()}",
            ["type"] = "Like",
            ["actor"] = actorUrl,
            ["object"] = postUrl
        };
        
        return await SendActivityToInboxAsync(activity, targetActor.InboxUri, actorUrl);
    }

    public async Task<bool> SendUndoActivityAsync(
        string activityType,
        string objectUri,
        Guid publisherId
    )
    {
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return false;
        
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var followers = await GetRemoteFollowersAsync(publisher.Id);
        
        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/undo/{Guid.NewGuid()}",
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = activityType,
                ["object"] = objectUri
            }
        };
        
        var successCount = 0;
        foreach (var follower in followers)
        {
            if (follower.InboxUri != null)
            {
                var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
                if (success)
                    successCount++;
            }
        }
        
        return successCount > 0;
    }

    private async Task<bool> SendActivityToInboxAsync(
        Dictionary<string, object> activity,
        string inboxUrl,
        string actorUri
    )
    {
        try
        {
            var json = JsonSerializer.Serialize(activity);
            var request = new HttpRequestMessage(HttpMethod.Post, inboxUrl);
            
            request.Content = new StringContent(json, Encoding.UTF8, "application/activity+json");
            request.Headers.Date = DateTimeOffset.UtcNow;
            
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(bodyBytes);
            var digest = $"SHA-256={Convert.ToBase64String(hash)}";
            request.Headers.Add("Digest", digest);
            
            var signatureHeaders = await signatureService.SignOutgoingRequest(request, actorUri);
            var signature = signatureHeaders;
            
            var signatureString = $"keyId=\"{signature["keyId"]}\"," +
                               $"algorithm=\"{signature["algorithm"]}\"," +
                               $"headers=\"{signature["headers"]}\"," +
                               $"signature=\"{signature["signature"]}\"";
            
            request.Headers.Add("Signature", signatureString);
            request.Headers.Add("Host", new Uri(inboxUrl).Host);
            
            var response = await HttpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to send activity to {Inbox}. Status: {Status}, Response: {Response}",
                    inboxUrl, response.StatusCode, responseContent);
                return false;
            }
            
            logger.LogInformation("Successfully sent activity to {Inbox}", inboxUrl);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending activity to {Inbox}", inboxUrl);
            return false;
        }
    }

    private async Task<List<SnFediverseActor>> GetRemoteFollowersAsync(Guid publisherId)
    {
        return await db.FediverseRelationships
            .Include(r => r.TargetActor)
            .Where(r => 
                r.LocalPublisherId == publisherId && 
                r.IsFollowedBy && 
                r.IsLocalActor)
            .Select(r => r.TargetActor)
            .ToListAsync();
    }

    private async Task<SnFediverseActor?> GetOrCreateLocalActorAsync(SnPublisher publisher)
    {
        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        
        var localActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUrl);
        
        if (localActor != null)
            return localActor;
        
        var instance = await db.FediverseInstances
            .FirstOrDefaultAsync(i => i.Domain == Domain);
        
        if (instance == null)
        {
            instance = new SnFediverseInstance
            {
                Domain = Domain,
                Name = Domain
            };
            db.FediverseInstances.Add(instance);
            await db.SaveChangesAsync();
        }

        var assetsBaseUrl = configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";
        
        localActor = new SnFediverseActor
        {
            Uri = actorUrl,
            Username = publisher.Name,
            DisplayName = publisher.Name,
            Bio = publisher.Bio,
            InboxUri = $"{actorUrl}/inbox",
            OutboxUri = $"{actorUrl}/outbox",
            FollowersUri = $"{actorUrl}/followers",
            FollowingUri = $"{actorUrl}/following",
            AvatarUrl = publisher.Picture != null ? $"{assetsBaseUrl}/{publisher.Picture.Id}" : null,
            HeaderUrl = publisher.Background != null ? $"{assetsBaseUrl}/{publisher.Background.Id}" : null,
            InstanceId = instance.Id
        };
        
        db.FediverseActors.Add(localActor);
        await db.SaveChangesAsync();
        
        return localActor;
    }

    private async Task<SnFediverseActor?> GetOrFetchActorAsync(string actorUri)
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUri);
        
        if (actor != null)
            return actor;
        
        try
        {
            var response = await HttpClient.GetAsync(actorUri);
            if (!response.IsSuccessStatusCode)
                return null;
            
            var json = await response.Content.ReadAsStringAsync();
            var actorData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            
            if (actorData == null)
                return null;
            
            var domain = new Uri(actorUri).Host;
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
                await discoveryService.FetchInstanceMetadataAsync(instance);
            }
            
            actor = new SnFediverseActor
            {
                Uri = actorUri,
                Username = ExtractUsername(actorUri),
                DisplayName = actorData.GetValueOrDefault("name")?.ToString(),
                Bio = actorData.GetValueOrDefault("summary")?.ToString(),
                InboxUri = actorData.GetValueOrDefault("inbox")?.ToString(),
                OutboxUri = actorData.GetValueOrDefault("outbox")?.ToString(),
                FollowersUri = actorData.GetValueOrDefault("followers")?.ToString(),
                FollowingUri = actorData.GetValueOrDefault("following")?.ToString(),
                AvatarUrl = actorData.GetValueOrDefault("icon")?.ToString(),
                InstanceId = instance.Id
            };
            
            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();
            
            return actor;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch actor: {Uri}", actorUri);
            return null;
        }
    }

    private string ExtractUsername(string actorUri)
    {
        return actorUri.Split('/').Last();
    }
}
