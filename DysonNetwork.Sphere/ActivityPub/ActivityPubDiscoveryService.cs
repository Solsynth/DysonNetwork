using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DysonNetwork.Sphere.ActivityPub;

public partial class ActivityPubDiscoveryService(
    AppDatabase db,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ActivityPubDiscoveryService> logger
)
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private HttpClient HttpClient => httpClientFactory.CreateClient();

    private static readonly Regex HandlePattern = HandleRegex();

    public async Task<SnFediverseActor?> DiscoverActorAsync(string query)
    {
        var (username, domain) = ParseHandle(query);
        if (username == null || domain == null)
            return null;

        var (actorUri, avatarUrl) = await GetActorUriFromWebfingerAsync(username, domain);
        if (actorUri == null)
            return null;

        return await StoreActorAsync(actorUri, username, domain, avatarUrl);
    }

    public async Task<List<SnFediverseActor>> SearchActorsAsync(
        string query,
        int limit = 20,
        bool includeRemoteDiscovery = true
    )
    {
        var localResults = await db.FediverseActors
            .Include(a => a.Instance)
            .Where(a =>
                a.Username.Contains(query) ||
                a.DisplayName != null && a.DisplayName.Contains(query))
            .OrderByDescending(a => a.LastActivityAt ?? a.CreatedAt)
            .Take(limit)
            .ToListAsync();

        if (!includeRemoteDiscovery)
            return localResults;

        var (username, domain) = ParseHandle(query);
        if (username == null || domain == null) return localResults;
        {
            var (actorUri, avatarUrl) = await GetActorUriFromWebfingerAsync(username, domain);
            if (actorUri == null) return localResults;
            var remoteActor = await StoreActorAsync(actorUri, username, domain, avatarUrl);
            if (remoteActor == null || localResults.Any(a => a.Uri == actorUri)) return localResults;
            var combined = new List<SnFediverseActor>(localResults) { remoteActor };
            return combined.Take(limit).ToList();
        }
    }

    private (string? username, string? domain) ParseHandle(string query)
    {
        var match = HandlePattern.Match(query.Trim());
        return !match.Success ? (null, null) : (match.Groups[1].Value, match.Groups[2].Value);
    }

    private async Task<(string? actorUri, string? avatarUrl)> GetActorUriFromWebfingerAsync(string username,
        string domain)
    {
        if (domain == Domain)
            return (null, null);

        try
        {
            var webfingerUrl = $"https://{domain}/.well-known/webfinger?resource=acct:{username}@{domain}";
            logger.LogInformation("Querying Webfinger: {Url}", webfingerUrl);

            var response = await HttpClient.GetAsync(webfingerUrl);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Webfinger request failed: {Url} - {StatusCode}", webfingerUrl, response.StatusCode);
                return (null, null);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            logger.LogDebug("Webfinger response Content-Type: {ContentType}", contentType);

            var content = await response.Content.ReadAsStringAsync();
            logger.LogDebug("Webfinger response from {Url}: {Content}", webfingerUrl, content);

            (string? actorUri, string? avatarUrl) result;

            if (contentType?.Contains("json") == true)
            {
                result = ParseJsonWebfingerResponse(content, webfingerUrl);
            }
            else if (contentType?.Contains("xml") == true || content.TrimStart().StartsWith("<?xml"))
            {
                result = ParseXmlWebfingerResponse(content, webfingerUrl);
            }
            else
            {
                logger.LogWarning("Unknown Content-Type from {Url}: {ContentType}, trying JSON parsing", webfingerUrl,
                    contentType);
                result = ParseJsonWebfingerResponse(content, webfingerUrl);
            }

            if (result.actorUri == null)
            {
                logger.LogWarning("Failed to extract actor URI from Webfinger response from {Url}", webfingerUrl);
                return (null, null);
            }

            logger.LogInformation("Found actor URI via Webfinger: {ActorUri}, Avatar: {AvatarUrl}", result.actorUri,
                result.avatarUrl);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying Webfinger for {Username}@{Domain}", username, domain);
            return (null, null);
        }
    }

    private (string? actorUri, string? avatarUrl) ParseJsonWebfingerResponse(string json, string sourceUrl)
    {
        try
        {
            var webfingerData = JsonSerializer.Deserialize<WebFingerResponse>(json);

            if (webfingerData?.Links == null)
            {
                logger.LogWarning("Invalid JSON Webfinger response from {Url}", sourceUrl);
                return (null, null);
            }

            logger.LogDebug("Found {LinkCount} links in JSON Webfinger response", webfingerData.Links.Count);
            foreach (var link in webfingerData.Links)
            {
                logger.LogDebug("Link: rel={Rel}, type={Type}, href={Href}", link.Rel, link.Type, link.Href);
            }

            var selfLink = webfingerData.Links.FirstOrDefault(l =>
                l is { Rel: "self", Type: "application/activity+json" });

            if (selfLink == null)
            {
                logger.LogWarning("No self link found in JSON Webfinger response from {Url}", sourceUrl);
                return (null, null);
            }

            var avatarLink = webfingerData.Links.FirstOrDefault(l =>
                l.Rel == "http://webfinger.net/rel/avatar");

            return (selfLink.Href, avatarLink?.Href);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing JSON Webfinger response from {Url}", sourceUrl);
            return (null, null);
        }
    }

    private (string? actorUri, string? avatarUrl) ParseXmlWebfingerResponse(string xml, string sourceUrl)
    {
        try
        {
            var xDoc = XDocument.Parse(xml);
            var xNamespace = XNamespace.Get("http://docs.oasis-open.org/ns/xri/xrd-1.0");

            var links = xDoc.Descendants(xNamespace + "Link").ToList();
            logger.LogDebug("Found {LinkCount} links in XML Webfinger response", links.Count);

            string? actorUri = null;
            string? avatarUrl = null;

            foreach (var link in links)
            {
                var rel = link.Attribute("rel")?.Value;
                var type = link.Attribute("type")?.Value;
                var href = link.Attribute("href")?.Value;
                logger.LogDebug("XML Link: rel={Rel}, type={Type}, href={Href}", rel, type, href);

                if (rel == "self" && type == "application/activity+json" && href != null)
                {
                    actorUri = href;
                }

                if (rel == "http://webfinger.net/rel/avatar" && href != null)
                {
                    avatarUrl = href;
                }
            }

            if (actorUri == null)
            {
                logger.LogWarning("No self link found in XML Webfinger response from {Url}", sourceUrl);
                return (null, null);
            }

            return (actorUri, avatarUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing XML Webfinger response from {Url}", sourceUrl);
            return (null, null);
        }
    }

    private async Task<SnFediverseActor?> StoreActorAsync(
        string actorUri,
        string username,
        string domain,
        string? webfingerAvatarUrl
    )
    {
        var existingActor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (existingActor != null)
            return existingActor;

        try
        {
            logger.LogInformation("Storing actor from Webfinger: {ActorUri}", actorUri);

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
                Username = username,
                AvatarUrl = webfingerAvatarUrl,
                InstanceId = instance.Id,
                LastFetchedAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
            };

            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();

            logger.LogInformation("Successfully stored actor from Webfinger: {Username}@{Domain}", username, domain);
            
            await FetchActorDataAsync(actor);

            actor.Instance = instance;
            return actor;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error storing actor: {Uri}", actorUri);
            return null;
        }
    }

    private async Task FetchActorDataAsync(SnFediverseActor actor)
    {
        try
        {
            logger.LogInformation("Attempting to fetch additional actor data from: {ActorUri}", actor.Uri);

            var request = new HttpRequestMessage(HttpMethod.Get, actor.Uri);
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/activity+json"));

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to fetch actor data: {Url} - {StatusCode}, using Webfinger data only",
                    actor.Uri, response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            var actorData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            if (actorData == null)
            {
                logger.LogWarning("Invalid actor response from {Url}, using Webfinger data only", actor.Uri);
                return;
            }

            actor.Type = actorData.GetValueOrDefault("type")?.ToString() ?? "Person";
            actor.DisplayName = actorData.GetValueOrDefault("name")?.ToString();
            actor.Bio = actorData.GetValueOrDefault("summary")?.ToString();
            actor.InboxUri = actorData.GetValueOrDefault("inbox")?.ToString();
            actor.OutboxUri = actorData.GetValueOrDefault("outbox")?.ToString();
            actor.FollowersUri = actorData.GetValueOrDefault("followers")?.ToString();
            actor.FollowingUri = actorData.GetValueOrDefault("following")?.ToString();
            actor.FeaturedUri = actorData.GetValueOrDefault("featured")?.ToString();
            actor.AvatarUrl = ExtractAvatarUrl(actorData.GetValueOrDefault("icon")) ?? actor.AvatarUrl;
            actor.HeaderUrl = ExtractImageUrl(actorData.GetValueOrDefault("image"));
            actor.PublicKeyId = ExtractPublicKeyId(actorData.GetValueOrDefault("publicKey"));
            actor.PublicKey = ExtractPublicKeyPem(actorData.GetValueOrDefault("publicKey"));
            actor.IsBot = actorData.GetValueOrDefault("type")?.ToString() == "Service";
            actor.IsLocked = actorData.GetValueOrDefault("manuallyApprovesFollowers")?.ToString() == "true";
            actor.IsDiscoverable = actorData.GetValueOrDefault("discoverable")?.ToString() != "false";

            // Store additional fields in Metadata
            var excludedKeys = new HashSet<string>
            {
                "id", "name", "summary", "preferredUsername", "inbox", "outbox", "followers", "following", "featured",
                "icon", "image", "publicKey", "type", "manuallyApprovesFollowers", "discoverable", "@context"
            };
            actor.Metadata = actorData.Where(kvp => !excludedKeys.Contains(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            await db.SaveChangesAsync();

            logger.LogInformation("Successfully fetched additional actor data for: {Username}", actor.Username);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch additional actor data for {Uri}, using Webfinger data only",
                actor.Uri);
        }
    }

    private static string? ExtractAvatarUrl(object? iconData)
    {
        return iconData switch
        {
            null => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element when element.TryGetProperty("url", out var urlElement) => urlElement.GetString(),
            _ => iconData.ToString()
        };
    }

    private string? ExtractImageUrl(object? imageData)
    {
        return ExtractAvatarUrl(imageData);
    }

    private static string? ExtractPublicKeyId(object? publicKeyData)
    {
        return publicKeyData switch
        {
            null => null,
            JsonElement element when element.TryGetProperty("id", out var idElement) => idElement.GetString(),
            Dictionary<string, object> dict when dict.TryGetValue("id", out var idValue) => idValue.ToString(),
            _ => null
        };
    }

    private static string? ExtractPublicKeyPem(object? publicKeyData)
    {
        return publicKeyData switch
        {
            null => null,
            JsonElement element when element.TryGetProperty("publicKeyPem", out var pemElement) =>
                pemElement.GetString(),
            Dictionary<string, object> dict when dict.TryGetValue("publicKeyPem", out var pemValue) => pemValue
                .ToString(),
            _ => null
        };
    }

    [GeneratedRegex(@"^@?(\w+)@([\w.-]+)$", RegexOptions.Compiled)]
    private static partial Regex HandleRegex();
}