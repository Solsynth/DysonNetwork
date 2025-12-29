using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DysonNetwork.Sphere.ActivityPub;

public class MastodonInstanceV2Response
{
    [JsonPropertyName("domain")] public string Domain { get; init; } = null!;
    [JsonPropertyName("title")] public string Title { get; init; } = null!;
    [JsonPropertyName("version")] public string Version { get; init; } = null!;
    [JsonPropertyName("source_url")] public string? SourceUrl { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("usage")] public MastodonUsage? Usage { get; init; }
    [JsonPropertyName("thumbnail")] public MastodonThumbnail? Thumbnail { get; init; }
    [JsonPropertyName("icon")] public List<MastodonIcon>? Icon { get; init; }
    [JsonPropertyName("languages")] public List<string>? Languages { get; init; }
    [JsonPropertyName("contact")] public MastodonContact? Contact { get; init; }
    [JsonPropertyName("registrations")] public Dictionary<string, object>? Registrations { get; init; }
    [JsonPropertyName("configuration")] public Dictionary<string, object>? Configuration { get; init; }
}

public class MastodonUsage
{
    [JsonPropertyName("users")] public MastodonUserUsage? Users { get; init; }
}

public class MastodonUserUsage
{
    [JsonPropertyName("active_month")] public int ActiveMonth { get; init; }
}

public class MastodonThumbnail
{
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public class MastodonIcon
{
    [JsonPropertyName("src")] public string? Src { get; init; }
    [JsonPropertyName("size")] public string? Size { get; init; }
}

public class MastodonContact
{
    [JsonPropertyName("email")] public string? Email { get; init; }
    [JsonPropertyName("account")] public MastodonContactAccount? Account { get; init; }
}

public class MastodonContactAccount
{
    [JsonPropertyName("username")] public string? Username { get; init; }
}

public class MisskeyMetaResponse
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("uri")] public string? Uri { get; init; }
    [JsonPropertyName("langs")] public List<string>? Langs { get; init; }
    [JsonPropertyName("maintainerName")] public string? MaintainerName { get; init; }
    [JsonPropertyName("maintainerEmail")] public string? MaintainerEmail { get; init; }
    [JsonPropertyName("iconUrl")] public string? IconUrl { get; init; }
    [JsonPropertyName("bannerUrl")] public string? BannerUrl { get; init; }
    [JsonPropertyName("repositoryUrl")] public string? RepositoryUrl { get; init; }
    [JsonPropertyName("privacyPolicyUrl")] public string? PrivacyPolicyUrl { get; init; }
    [JsonPropertyName("tosUrl")] public string? TosUrl { get; init; }

    [JsonPropertyName("maxNoteTextLength")]
    public int? MaxNoteTextLength { get; init; }
}

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
            .Where(a => a.Instance.Domain != Domain) // Exclude localhost
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

    public async Task FetchInstanceMetadataAsync(SnFediverseInstance instance)
    {
        if (instance.MetadataFetchedAt != null)
            return;

        try
        {
            logger.LogInformation("Fetching instance metadata for {Domain}", instance.Domain);

            var mastodonSuccess = await FetchMastodonMetadataAsync(instance);

            if (!mastodonSuccess)
            {
                logger.LogInformation("Mastodon API not available for {Domain}, trying Misskey API", instance.Domain);
                var misskeySuccess = await FetchMisskeyMetadataAsync(instance);

                if (!misskeySuccess)
                {
                    logger.LogInformation("No compatible API found for {Domain}", instance.Domain);
                    return;
                }
            }

            instance.MetadataFetchedAt = NodaTime.SystemClock.Instance.GetCurrentInstant();
            await db.SaveChangesAsync();

            logger.LogInformation("Successfully fetched instance metadata for {Domain} (Software: {Software})",
                instance.Domain, instance.Software);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch instance metadata for {Domain}", instance.Domain);
        }
    }

    private async Task<bool> FetchMastodonMetadataAsync(SnFediverseInstance instance)
    {
        try
        {
            var apiUrl = $"https://{instance.Domain}/api/v2/instance";
            var response = await HttpClient.GetAsync(apiUrl);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Mastodon API not available for {Domain} (Status: {StatusCode})",
                    instance.Domain, response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<MastodonInstanceV2Response>(content);

            if (apiResponse == null)
            {
                logger.LogWarning("Failed to parse Mastodon API response for {Domain}", instance.Domain);
                return false;
            }

            instance.Name = apiResponse.Title;
            instance.Description = apiResponse.Description;
            instance.Software = "Mastodon";
            instance.Version = apiResponse.Version;
            instance.ThumbnailUrl = apiResponse.Thumbnail?.Url;

            if (apiResponse.Icon != null && apiResponse.Icon.Count > 0)
            {
                var largestIcon = apiResponse.Icon
                    .Where(i => i.Src != null)
                    .OrderByDescending(i => GetIconSizePixels(i.Size))
                    .FirstOrDefault();
                instance.IconUrl = largestIcon?.Src;
            }

            instance.ContactEmail = apiResponse.Contact?.Email;
            instance.ContactAccountUsername = apiResponse.Contact?.Account?.Username;
            instance.ActiveUsers = apiResponse.Usage?.Users?.ActiveMonth;

            var metadata = new Dictionary<string, object>();

            if (apiResponse.Languages != null && apiResponse.Languages.Count > 0)
                metadata["languages"] = apiResponse.Languages;

            if (apiResponse.SourceUrl != null)
                metadata["source_url"] = apiResponse.SourceUrl;

            if (apiResponse.Registrations != null)
                metadata["registrations"] = apiResponse.Registrations;

            if (apiResponse.Configuration != null)
            {
                var filteredConfig = new Dictionary<string, object>();
                if (apiResponse.Configuration.TryGetValue("media_attachments", out var mediaConfig))
                    filteredConfig["media_attachments"] = mediaConfig;
                if (apiResponse.Configuration.TryGetValue("polls", out var pollConfig))
                    filteredConfig["polls"] = pollConfig;
                if (apiResponse.Configuration.TryGetValue("translation", out var translationConfig))
                    filteredConfig["translation"] = translationConfig;
                metadata["configuration"] = filteredConfig;
            }

            if (metadata.Count > 0)
                instance.Metadata = metadata;

            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch Mastodon metadata for {Domain}", instance.Domain);
            return false;
        }
    }

    private async Task<bool> FetchMisskeyMetadataAsync(SnFediverseInstance instance)
    {
        try
        {
            var apiUrl = $"https://{instance.Domain}/api/meta";
            var response = await HttpClient.PostAsync(apiUrl, null);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Misskey API not available for {Domain} (Status: {StatusCode})",
                    instance.Domain, response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonSerializer.Deserialize<MisskeyMetaResponse>(content);

            if (apiResponse == null)
            {
                logger.LogWarning("Failed to parse Misskey API response for {Domain}", instance.Domain);
                return false;
            }

            instance.Name = apiResponse.Name;
            instance.Description = apiResponse.Description;
            instance.Software = "Misskey";
            instance.Version = apiResponse.Version;
            instance.IconUrl = apiResponse.IconUrl;
            instance.ThumbnailUrl = apiResponse.BannerUrl;
            instance.ContactEmail = apiResponse.MaintainerEmail;
            instance.ContactAccountUsername = apiResponse.MaintainerName;

            var metadata = new Dictionary<string, object>();

            if (apiResponse.Langs != null && apiResponse.Langs.Count > 0)
                metadata["languages"] = apiResponse.Langs;

            if (apiResponse.RepositoryUrl != null)
                metadata["source_url"] = apiResponse.RepositoryUrl;

            if (apiResponse.PrivacyPolicyUrl != null)
                metadata["privacy_policy_url"] = apiResponse.PrivacyPolicyUrl;

            if (apiResponse.TosUrl != null)
                metadata["terms_of_service_url"] = apiResponse.TosUrl;

            if (apiResponse.MaxNoteTextLength.HasValue)
                metadata["max_note_text_length"] = apiResponse.MaxNoteTextLength.Value;

            if (apiResponse.Uri != null)
                metadata["uri"] = apiResponse.Uri;

            if (metadata.Count > 0)
                instance.Metadata = metadata;

            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch Misskey metadata for {Domain}", instance.Domain);
            return false;
        }
    }

    private static int GetIconSizePixels(string? size)
    {
        if (string.IsNullOrEmpty(size))
            return 0;

        var parts = size.Split('x');
        if (parts.Length != 2)
            return 0;

        if (int.TryParse(parts[0], out var width) && int.TryParse(parts[1], out var height))
            return width * height;

        return 0;
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
            await FetchInstanceMetadataAsync(instance);

            actor.Instance = instance;
            return actor;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error storing actor: {Uri}", actorUri);
            return null;
        }
    }

    public async Task FetchActorDataAsync(SnFediverseActor actor)
    {
        try
        {
            logger.LogInformation("Attempting to fetch additional actor data from: {ActorUri}", actor.Uri);

            var request = new HttpRequestMessage(HttpMethod.Get, actor.Uri);
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/activity+json")
            );

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
