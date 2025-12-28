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
        if (username == null || domain == null) return localResults;
        {
            var actorUri = await GetActorUriFromWebfingerAsync(username, domain);
            if (actorUri == null) return localResults;
            var remoteActor = await FetchAndStoreActorAsync(actorUri);
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

            var contentType = response.Content.Headers.ContentType?.MediaType;
            logger.LogDebug("Webfinger response Content-Type: {ContentType}", contentType);

            var content = await response.Content.ReadAsStringAsync();
            logger.LogDebug("Webfinger response from {Url}: {Content}", webfingerUrl, content);

            string? actorUri;

            if (contentType?.Contains("json") == true)
            {
                actorUri = ParseJsonWebfingerResponse(content, webfingerUrl);
            }
            else if (contentType?.Contains("xml") == true || content.TrimStart().StartsWith("<?xml"))
            {
                actorUri = ParseXmlWebfingerResponse(content, webfingerUrl);
            }
            else
            {
                logger.LogWarning("Unknown Content-Type from {Url}: {ContentType}, trying JSON parsing", webfingerUrl, contentType);
                actorUri = ParseJsonWebfingerResponse(content, webfingerUrl);
            }

            if (actorUri == null)
            {
                logger.LogWarning("Failed to extract actor URI from Webfinger response from {Url}", webfingerUrl);
                return null;
            }

            logger.LogInformation("Found actor URI via Webfinger: {ActorUri}", actorUri);
            return actorUri;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error querying Webfinger for {Username}@{Domain}", username, domain);
            return null;
        }
    }

    private string? ParseJsonWebfingerResponse(string json, string sourceUrl)
    {
        try
        {
            var webfingerData = JsonSerializer.Deserialize<WebFingerResponse>(json);

            if (webfingerData?.Links == null)
            {
                logger.LogWarning("Invalid JSON Webfinger response from {Url}", sourceUrl);
                return null;
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
                return null;
            }

            return selfLink.Href;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing JSON Webfinger response from {Url}", sourceUrl);
            return null;
        }
    }

    private string? ParseXmlWebfingerResponse(string xml, string sourceUrl)
    {
        try
        {
            var xDoc = XDocument.Parse(xml);
            var xNamespace = XNamespace.Get("http://docs.oasis-open.org/ns/xri/xrd-1.0");

            var links = xDoc.Descendants(xNamespace + "Link").ToList();
            logger.LogDebug("Found {LinkCount} links in XML Webfinger response", links.Count);

            foreach (var link in links)
            {
                var rel = link.Attribute("rel")?.Value;
                var type = link.Attribute("type")?.Value;
                var href = link.Attribute("href")?.Value;
                logger.LogDebug("XML Link: rel={Rel}, type={Type}, href={Href}", rel, type, href);

                if (rel == "self" && type == "application/activity+json" && href != null)
                {
                    return href;
                }
            }

            logger.LogWarning("No self link found in XML Webfinger response from {Url}", sourceUrl);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing XML Webfinger response from {Url}", sourceUrl);
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