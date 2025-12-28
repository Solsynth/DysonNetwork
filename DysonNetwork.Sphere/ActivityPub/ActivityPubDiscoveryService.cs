using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DysonNetwork.Sphere.ActivityPub;

public class ActivityPubDiscoveryService(
    AppDatabase db,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ActivityPubDiscoveryService> logger
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private HttpClient HttpClient => httpClientFactory.CreateClient();

    private static readonly Regex HandlePattern = new(@"^@?(\w+)@([\w.-]+)$", RegexOptions.Compiled);

    public async Task<SnFediverseActor?> DiscoverActorAsync(string query)
    {
        var (username, domain) = ParseHandle(query);
        if (username == null || domain == null)
            return null;

        var actorUri = await GetActorUriFromWebfingerAsync(username, domain);
        if (actorUri == null)
            return null;

        return await FetchAndStoreActorAsync(actorUri);
    }

    public async Task<List<SnFediverseActor>> SearchActorsAsync(
        string query,
        int limit = 20,
        bool includeRemoteDiscovery = true
    )
    {
        var localResults = await db.FediverseActors
            .Where(a =>
                a.Username.Contains(query) ||
                a.DisplayName != null && a.DisplayName.Contains(query))
            .OrderByDescending(a => a.LastActivityAt ?? a.CreatedAt)
            .Take(limit)
            .ToListAsync();

        if (!includeRemoteDiscovery)
            return localResults;

        var (username, domain) = ParseHandle(query);
        if (username != null && domain != null)
        {
            var actorUri = await GetActorUriFromWebfingerAsync(username, domain);
            if (actorUri != null)
            {
                var remoteActor = await FetchAndStoreActorAsync(actorUri);
                if (remoteActor != null && !localResults.Any(a => a.Uri == actorUri))
                {
                    var combined = new List<SnFediverseActor>(localResults) { remoteActor };
                    return combined.Take(limit).ToList();
                }
            }
        }

        return localResults;
    }

    private (string? username, string? domain) ParseHandle(string query)
    {
        var match = HandlePattern.Match(query.Trim());
        if (!match.Success)
            return (null, null);

        return (match.Groups[1].Value, match.Groups[2].Value);
    }

    private async Task<string?> GetActorUriFromWebfingerAsync(string username, string domain)
    {
        if (domain == Domain)
            return null;

        try
        {
            var webfingerUrl = $"https://{domain}/.well-known/webfinger?resource=acct:{username}@{domain}";
            logger.LogInformation("Querying Webfinger: {Url}", webfingerUrl);

            var response = await HttpClient.GetAsync(webfingerUrl);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Webfinger request failed: {Url} - {StatusCode}", webfingerUrl, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var webfingerData = JsonSerializer.Deserialize<WebFingerResponse>(json);

            if (webfingerData?.Links == null)
            {
                logger.LogWarning("Invalid Webfinger response from {Url}", webfingerUrl);
                return null;
            }

            var selfLink = webfingerData.Links.FirstOrDefault(l =>
                l.Rel == "self" &&
                l.Type == "application/activity+json");

            if (selfLink == null)
            {
                logger.LogWarning("No self link found in Webfinger response from {Url}", webfingerUrl);
                return null;
            }

            logger.LogInformation("Found actor URI via Webfinger: {ActorUri}", selfLink.Href);
            return selfLink.Href;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying Webfinger for {Username}@{Domain}", username, domain);
            return null;
        }
    }

    private async Task<SnFediverseActor?> FetchAndStoreActorAsync(string actorUri)
    {
        var existingActor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (existingActor != null)
            return existingActor;

        try
        {
            logger.LogInformation("Fetching actor: {ActorUri}", actorUri);

            var response = await HttpClient.GetAsync(actorUri);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch actor: {Url} - {StatusCode}", actorUri, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var actorData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (actorData == null)
            {
                logger.LogWarning("Invalid actor response from {Url}", actorUri);
                return null;
            }

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

            var actor = new SnFediverseActor
            {
                Uri = actorUri,
                Username = actorData.GetValueOrDefault("preferredUsername")?.ToString() ?? ExtractUsername(actorUri),
                DisplayName = actorData.GetValueOrDefault("name")?.ToString(),
                Bio = actorData.GetValueOrDefault("summary")?.ToString(),
                InboxUri = actorData.GetValueOrDefault("inbox")?.ToString(),
                OutboxUri = actorData.GetValueOrDefault("outbox")?.ToString(),
                FollowersUri = actorData.GetValueOrDefault("followers")?.ToString(),
                FollowingUri = actorData.GetValueOrDefault("following")?.ToString(),
                FeaturedUri = actorData.GetValueOrDefault("featured")?.ToString(),
                AvatarUrl = ExtractAvatarUrl(actorData.GetValueOrDefault("icon")),
                HeaderUrl = ExtractImageUrl(actorData.GetValueOrDefault("image")),
                PublicKeyId = ExtractPublicKeyId(actorData.GetValueOrDefault("publicKey")),
                PublicKey = ExtractPublicKeyPem(actorData.GetValueOrDefault("publicKey")),
                IsBot = actorData.GetValueOrDefault("type")?.ToString() == "Service",
                IsLocked = actorData.GetValueOrDefault("manuallyApprovesFollowers")?.ToString() == "true",
                IsDiscoverable = actorData.GetValueOrDefault("discoverable")?.ToString() != "false",
                InstanceId = instance.Id,
                LastFetchedAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
            };

            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();

            logger.LogInformation("Successfully fetched and stored actor: {Username}@{Domain}", actor.Username, domain);
            return actor;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching actor: {Uri}", actorUri);
            return null;
        }
    }

    private string ExtractUsername(string actorUri)
    {
        return actorUri.Split('/').Last();
    }

    private string? ExtractAvatarUrl(object? iconData)
    {
        if (iconData == null)
            return null;

        if (iconData is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString();

            if (element.TryGetProperty("url", out var urlElement))
                return urlElement.GetString();
        }

        return iconData.ToString();
    }

    private string? ExtractImageUrl(object? imageData)
    {
        return ExtractAvatarUrl(imageData);
    }

    private string? ExtractPublicKeyId(object? publicKeyData)
    {
        if (publicKeyData == null)
            return null;

        if (publicKeyData is JsonElement element && element.TryGetProperty("id", out var idElement))
            return idElement.GetString();

        if (publicKeyData is Dictionary<string, object> dict && dict.TryGetValue("id", out var idValue))
            return idValue?.ToString();

        return null;
    }

    private string? ExtractPublicKeyPem(object? publicKeyData)
    {
        if (publicKeyData == null)
            return null;

        if (publicKeyData is JsonElement element && element.TryGetProperty("publicKeyPem", out var pemElement))
            return pemElement.GetString();

        if (publicKeyData is Dictionary<string, object> dict && dict.TryGetValue("publicKeyPem", out var pemValue))
            return pemValue?.ToString();

        return null;
    }
}
