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
    private string AssetsBaseUrl => configuration["ActivityPub:FileBaseUrl"] ?? $"https://{Domain}/files";

    private HttpClient HttpClient
    {
        get
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Clear();
            return client;
        }
    }

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
        var localActor = await GetLocalActorAsync(publisher.Id);

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

        var existingRelationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r =>
                r.ActorId == localActor.Id &&
                r.TargetActorId == targetActor.Id);

        if (existingRelationship == null)
        {
            existingRelationship = new SnFediverseRelationship
            {
                ActorId = localActor.Id,
                TargetActorId = targetActor.Id,
                State = RelationshipState.Pending,
                IsFollowing = true,
                IsFollowedBy = false
            };
            db.FediverseRelationships.Add(existingRelationship);
        }
        else
        {
            existingRelationship.IsFollowing = true;
            existingRelationship.State = RelationshipState.Pending;
        }

        await db.SaveChangesAsync();

        return await SendActivityToInboxAsync(activity, targetActor.InboxUri, actorUrl);
    }

    public async Task<bool> SendUnfollowActivityAsync(
        Guid publisherId,
        string targetActorUri
    )
    {
        var publisher = await db.Publishers.FindAsync(publisherId);
        if (publisher == null)
            return false;

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var targetActor = await GetOrFetchActorAsync(targetActorUri);
        var localActor = await GetLocalActorAsync(publisher.Id);

        if (targetActor?.InboxUri == null || localActor == null)
        {
            logger.LogWarning("Target actor or inbox not found: {Uri}", targetActorUri);
            return false;
        }

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/undo/{Guid.NewGuid()}",
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Follow",
                ["object"] = targetActor.InboxUri
            }
        };

        var relationship = await db.FediverseRelationships
            .FirstOrDefaultAsync(r =>
                r.ActorId == localActor.Id &&
                r.TargetActorId == targetActor.Id);
        if (relationship == null) return false;

        var success = await SendActivityToInboxAsync(activity, targetActor.InboxUri, actorUrl);
        if (!success) return success;
        
        db.Remove(relationship);
        await db.SaveChangesAsync();

        return success;
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

        var followers = await GetRemoteFollowersAsync();
        var successCount = 0;

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
            if (success)
                successCount++;
        }

        logger.LogInformation("Sent Create activity to {Count}/{Total} followers",
            successCount, followers.Count);

        return successCount > 0;
    }

    public async Task<bool> SendUpdateActivityAsync(SnPost post)
    {
        var publisher = await db.Publishers.FindAsync(post.PublisherId);
        if (publisher == null)
            return false;

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var postUrl = $"https://{Domain}/posts/{post.Id}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{postUrl}/activity/{Guid.NewGuid()}",
            ["type"] = "Update",
            ["actor"] = actorUrl,
            ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = new Dictionary<string, object>
            {
                ["id"] = postUrl,
                ["type"] = post.Type == PostType.Article ? "Article" : "Note",
                ["published"] = (post.PublishedAt ?? post.CreatedAt).ToDateTimeOffset(),
                ["updated"] = post.EditedAt?.ToDateTimeOffset() ?? new DateTimeOffset(),
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

        var followers = await GetRemoteFollowersAsync();
        var successCount = 0;

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
            if (success)
                successCount++;
        }

        logger.LogInformation("Sent Update activity to {Count}/{Total} followers",
            successCount, followers.Count);

        return successCount > 0;
    }

    public async Task<bool> SendDeleteActivityAsync(SnPost post)
    {
        var publisher = await db.Publishers.FindAsync(post.PublisherId);
        if (publisher == null)
            return false;

        var actorUrl = $"https://{Domain}/activitypub/actors/{publisher.Name}";
        var postUrl = $"https://{Domain}/posts/{post.Id}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{postUrl}/delete/{Guid.NewGuid()}",
            ["type"] = "Delete",
            ["actor"] = actorUrl,
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = new Dictionary<string, object>
            {
                ["id"] = postUrl,
                ["type"] = "Tombstone"
            }
        };

        var followers = await GetRemoteFollowersAsync();
        var successCount = 0;

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
            if (success)
                successCount++;
        }

        logger.LogInformation("Sent Delete activity to {Count}/{Total} followers",
            successCount, followers.Count);

        return successCount > 0;
    }

    public async Task<bool> SendUpdateActorActivityAsync(SnFediverseActor actor)
    {
        var publisher = await db.Publishers
            .FirstOrDefaultAsync(p => p.Id == actor.PublisherId);

        if (publisher == null)
            return false;

        var actorUrl = actor.Uri;

        var actorObject = new Dictionary<string, object?>
        {
            ["id"] = actorUrl,
            ["type"] = actor.Type,
            ["name"] = publisher.Nick,
            ["preferredUsername"] = publisher.Name,
            ["summary"] = publisher.Bio ?? "",
            ["published"] = publisher.CreatedAt.ToDateTimeOffset(),
            ["updated"] = publisher.UpdatedAt.ToDateTimeOffset(),
            ["inbox"] = actor.InboxUri,
            ["outbox"] = actor.OutboxUri,
            ["followers"] = actor.FollowersUri,
            ["following"] = actor.FollowingUri,
            ["publicKey"] = new Dictionary<string, object?>
            {
                ["id"] = actor.PublicKeyId,
                ["owner"] = actorUrl,
                ["publicKeyPem"] = actor.PublicKey
            }
        };

        if (publisher.Picture != null)
        {
            actorObject["icon"] = new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["mediaType"] = publisher.Picture.MimeType,
                ["url"] = $"{AssetsBaseUrl}/{publisher.Picture.Id}"
            };
        }

        if (publisher.Background != null)
        {
            actorObject["image"] = new Dictionary<string, object?>
            {
                ["type"] = "Image",
                ["mediaType"] = publisher.Background.MimeType,
                ["url"] = $"{AssetsBaseUrl}/{publisher.Background.Id}"
            };
        }

        var activity = new Dictionary<string, object>
        {
            ["@context"] = new List<object>
            {
                "https://www.w3.org/ns/activitystreams",
                "https://w3id.org/security/v1"
            },
            ["id"] = $"{actorUrl}#update-{Guid.NewGuid()}",
            ["type"] = "Update",
            ["actor"] = actorUrl,
            ["published"] = DateTimeOffset.UtcNow,
            ["to"] = Array.Empty<object>(),
            ["cc"] = new[] { $"{actorUrl}/followers" },
            ["object"] = actorObject
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);

        var successCount = 0;

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
            if (success)
                successCount++;
        }

        logger.LogInformation("Sent Update actor activity to {Count}/{Total} followers",
            successCount, followers.Count);

        return successCount > 0;
    }

    public async Task<bool> SendLikeActivityToLocalPostAsync(
        Guid publisherId,
        Guid postId,
        Guid actorId
    )
    {
        var publisher = await db.Publishers
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == publisherId);

        if (publisher == null)
            return false;

        var publisherActor = await GetLocalActorAsync(publisherId);
        if (publisherActor == null)
            return false;

        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Id == actorId);

        if (actor == null)
            return false;

        var actorUrl = publisherActor.Uri;
        var postUrl = $"https://{Domain}/posts/{postId}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/likes/{Guid.NewGuid()}",
            ["type"] = "Like",
            ["actor"] = actor.Uri,
            ["object"] = postUrl,
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers" }
        };

        var followers = await GetRemoteFollowersAsync(actor.Id);

        var successCount = 0;

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
            if (success)
                successCount++;
        }

        logger.LogInformation("Sent Like activity for post {PostId} to {Count}/{Total} followers",
            postId, successCount, followers.Count);

        return successCount > 0;
    }

    public async Task<bool> SendUndoLikeActivityAsync(
        Guid publisherId,
        Guid postId,
        string likeActivityId
    )
    {
        var publisher = await db.Publishers
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Id == publisherId);

        if (publisher == null)
            return false;

        var localActor = await GetLocalActorAsync(publisherId);
        if (localActor == null)
            return false;

        var actorUrl = localActor.Uri;
        var postUrl = $"https://{Domain}/posts/{postId}";

        var activity = new Dictionary<string, object>
        {
            ["@context"] = "https://www.w3.org/ns/activitystreams",
            ["id"] = $"{actorUrl}/undo/{Guid.NewGuid()}",
            ["type"] = "Undo",
            ["actor"] = actorUrl,
            ["object"] = new Dictionary<string, object>
            {
                ["type"] = "Like",
                ["object"] = postUrl
            },
            ["to"] = new[] { "https://www.w3.org/ns/activitystreams#Public" },
            ["cc"] = new[] { $"{actorUrl}/followers" }
        };

        var followers = await GetRemoteFollowersAsync(localActor.Id);

        var successCount = 0;

        foreach (var follower in followers)
        {
            if (follower.InboxUri == null) continue;
            var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
            if (success)
                successCount++;
        }

        logger.LogInformation("Sent Undo Like activity for post {PostId} to {Count}/{Total} followers",
            postId, successCount, followers.Count);

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
        var followers = await GetRemoteFollowersAsync();

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
            if (follower.InboxUri == null) continue;
            var success = await SendActivityToInboxAsync(activity, follower.InboxUri, actorUrl);
            if (success)
                successCount++;
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
            var hash = SHA256.HashData(bodyBytes);
            var digest = $"SHA-256={Convert.ToBase64String(hash)}";
            request.Headers.Add("Digest", digest);
            request.Headers.Host = new Uri(inboxUrl).Host;

            logger.LogInformation("Preparing request to {Inbox}", inboxUrl);
            logger.LogInformation("Request body (truncated): {Body}", json[..Math.Min(200, json.Length)] + "...");
            logger.LogInformation("Request headers before signing: Date={Date}, Digest={Digest}, Host={Host}",
                request.Headers.Date, digest, request.Headers.Host);

            var signatureHeaders = await signatureService.SignOutgoingRequest(request, actorUri);

            var signatureString = $"keyId=\"{signatureHeaders["keyId"]}\"," +
                                  $"algorithm=\"{signatureHeaders["algorithm"]}\"," +
                                  $"headers=\"{signatureHeaders["headers"]}\"," +
                                  $"signature=\"{signatureHeaders["signature"]}\"";

            request.Headers.Add("Signature", signatureString);

            logger.LogInformation("Full signature header: {Signature}", signatureString);
            logger.LogInformation("Request headers after signing:");
            foreach (var header in request.Headers)
            {
                var value = header.Value.Any() ? header.Value.First() : string.Empty;
                if (header.Key == "signature")
                    value = value[..Math.Min(100, value.Length)] + "...";
                logger.LogInformation("  {Key}: {Value}", header.Key, value);
            }

            var response = await HttpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            logger.LogInformation("Response from {Inbox}. Status: {Status}", inboxUrl, response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Failed to send activity to {Inbox}. Status: {Status}, Response: {Response}",
                    inboxUrl, response.StatusCode, responseContent);
                logger.LogError("Full request details: Method={Method}, Uri={Uri}, ContentType={ContentType}",
                    request.Method, request.RequestUri, request.Content?.Headers.ContentType);
                return false;
            }

            logger.LogInformation("Successfully sent activity to {Inbox}", inboxUrl);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error sending activity to {Inbox}. Exception: {Message}", inboxUrl, ex.Message);
            return false;
        }
    }

    private async Task<List<SnFediverseActor>> GetRemoteFollowersAsync()
    {
        return await db.FediverseRelationships
            .Include(r => r.ActorId)
            .Where(r => r.IsFollowedBy)
            .Select(r => r.Actor)
            .ToListAsync();
    }
    
    private async Task<List<SnFediverseActor>> GetRemoteFollowersAsync(Guid actorId)
    {
        return await db.FediverseRelationships
            .Include(r => r.ActorId)
            .Where(r => r.TargetActorId == actorId && r.IsFollowedBy)
            .Select(r => r.Actor)
            .ToListAsync();
    }

    public async Task<SnFediverseActor?> GetLocalActorAsync(Guid publisherId)
    {
        return await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.PublisherId == publisherId);
    }

    public async Task<SnFediverseActor?> GetOrCreateLocalActorAsync(SnPublisher publisher)
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
            InstanceId = instance.Id,
            PublisherId = publisher.Id,
        };

        db.FediverseActors.Add(localActor);
        await db.SaveChangesAsync();

        return localActor;
    }

    private async Task<SnFediverseActor?> GetOrFetchActorAsync(string actorUri)
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor != null)
            return actor;

        try
        {
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
            }

            actor = new SnFediverseActor
            {
                Uri = actorUri,
                Username = ExtractUsername(actorUri),
                InstanceId = instance.Id,
                LastFetchedAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
            };

            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();

            await discoveryService.FetchActorDataAsync(actor);
            await discoveryService.FetchInstanceMetadataAsync(instance);

            actor.Instance = instance;
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