using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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

public class NodeInfoDiscoveryResponse
{
    [JsonPropertyName("links")] public List<NodeInfoLink>? Links { get; init; }
}

public class NodeInfoLink
{
    [JsonPropertyName("rel")] public string? Rel { get; init; }
    [JsonPropertyName("href")] public string? Href { get; init; }
}

public class NodeInfoResponse
{
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("software")] public NodeInfoSoftware? Software { get; init; }
    [JsonPropertyName("protocols")] public List<string>? Protocols { get; init; }
    [JsonPropertyName("services")] public NodeInfoServices? Services { get; init; }
    [JsonPropertyName("usage")] public NodeInfoUsage? Usage { get; init; }
    [JsonPropertyName("configuration")] public NodeInfoConfiguration? Configuration { get; init; }
    [JsonPropertyName("openRegistrations")] public bool? OpenRegistrations { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, object>? Metadata { get; init; }
}

public class NodeInfoSoftware
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
}

public class NodeInfoServices
{
    [JsonPropertyName("inbound")] public List<string>? Inbound { get; init; }
    [JsonPropertyName("outbound")] public List<string>? Outbound { get; init; }
}

public class NodeInfoUsage
{
    [JsonPropertyName("users")] public NodeInfoUsers? Users { get; init; }
    [JsonPropertyName("localPosts")] public int? LocalPosts { get; init; }
    [JsonPropertyName("localComments")] public int? LocalComments { get; init; }
}

public class NodeInfoUsers
{
    [JsonPropertyName("total")] public int? Total { get; init; }
    [JsonPropertyName("activeMonth")] public int? ActiveMonth { get; init; }
    [JsonPropertyName("activeHalfyear")] public int? ActiveHalfyear { get; init; }
}

public class NodeInfoConfiguration
{
    [JsonPropertyName("accounts")] public NodeInfoAccounts? Accounts { get; init; }
    [JsonPropertyName("statuses")] public NodeInfoStatuses? Statuses { get; init; }
    [JsonPropertyName("mediaAttachments")] public NodeInfoMediaAttachments? MediaAttachments { get; init; }
}

public class NodeInfoAccounts
{
    [JsonPropertyName("maxFeaturedObjects")] public int? MaxFeaturedObjects { get; init; }
}

public class NodeInfoStatuses
{
    [JsonPropertyName("maxCharacters")] public int? MaxCharacters { get; init; }
    [JsonPropertyName("maxMediaAttachments")] public int? MaxMediaAttachments { get; init; }
}

public class NodeInfoMediaAttachments
{
    [JsonPropertyName("supportedMimeTypes")] public List<string>? SupportedMimeTypes { get; init; }
    [JsonPropertyName("imageSizeLimit")] public int? ImageSizeLimit { get; init; }
    [JsonPropertyName("imageMatrixLimit")] public int? ImageMatrixLimit { get; init; }
    [JsonPropertyName("videoSizeLimit")] public int? VideoSizeLimit { get; init; }
    [JsonPropertyName("videoFrameRateLimit")] public int? VideoFrameRateLimit { get; init; }
    [JsonPropertyName("videoMatrixLimit")] public int? VideoMatrixLimit { get; init; }
}

public partial class ActivityPubDiscoveryService(
    AppDatabase db,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ActivityPubDiscoveryService> logger,
    ISignatureService signatureService,
    IFederationMetricsService? metricsService = null
) : IActorDiscoveryService
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private HttpClient HttpClient => httpClientFactory.CreateClient();

    private static readonly Regex HandlePattern = HandleRegex();

    private async Task SignRequestAsync(HttpRequestMessage request, Guid? publisherId = null)
    {
        if (publisherId.HasValue)
        {
            logger.LogDebug("Signing request with publisher key: {PublisherId}", publisherId.Value);
            await signatureService.SignOutgoingRequestAsync(request, publisherId.Value);
        }
        else
        {
            logger.LogDebug("Signing request with server root key for federation");
            await signatureService.SignOutgoingRequestWithServerKeyAsync(request);
        }
    }

    public async Task<SnFediverseActor?> DiscoverActorAsync(string query)
    {
        var (username, domain) = ParseHandle(query);
        if (username == null || domain == null)
            return null;

        var (actorUri, avatarUrl) = await GetActorUriFromWebfingerAsync(username, domain);
        if (actorUri == null)
            return null;

        return await GetActorFromWebfingerAsync(actorUri, username, domain, avatarUrl);
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
            var remoteActor = await GetActorFromWebfingerAsync(actorUri, username, domain, avatarUrl);
            if (remoteActor == null || localResults.Any(a => a.Uri == actorUri)) return localResults;
            var combined = new List<SnFediverseActor>(localResults) { remoteActor };
            return combined.Take(limit).ToList();
        }
    }

    private async Task<SnFediverseActor?> GetActorFromWebfingerAsync(
        string actorUri,
        string username,
        string domain,
        string? webfingerAvatarUrl
    )
    {
        try
        {
            logger.LogInformation("Getting actor from Webfinger: {ActorUri}", actorUri);

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
                await FetchInstanceMetadataAsync(instance);
            }

            // Check if we already have this actor in DB (including soft-deleted)
            var existingActor = await db.FediverseActors
                .IgnoreQueryFilters()
                .Include(a => a.Instance)
                .FirstOrDefaultAsync(a => a.Uri == actorUri);

            if (existingActor != null)
            {
                // If soft-deleted, restore it
                if (existingActor.DeletedAt != null)
                {
                    existingActor.DeletedAt = null;
                    await db.SaveChangesAsync();
                }
                
                // If we have the actor but bio is missing, try to refresh
                if (string.IsNullOrEmpty(existingActor.Bio) || string.IsNullOrEmpty(existingActor.DisplayName))
                {
                    logger.LogDebug("Actor exists but missing bio/displayname, refreshing: {ActorUri}", actorUri);
                    await FetchActorDataAsync(existingActor);
                }
                return existingActor;
            }

            // Create new actor and fetch full data
            var actor = new SnFediverseActor
            {
                Uri = actorUri,
                Username = username,
                AvatarUrl = webfingerAvatarUrl,
                InstanceId = instance.Id,
                Instance = instance,
                LastFetchedAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
            };

            try
            {
                db.FediverseActors.Add(actor);
                await db.SaveChangesAsync();
                await FetchActorDataAsync(actor);
                return actor;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                logger.LogInformation("Actor already exists (race condition), fetching: {ActorUri}", actorUri);
                var existing = await db.FediverseActors
                    .Include(a => a.Instance)
                    .FirstOrDefaultAsync(a => a.Uri == actorUri);
                
                if (existing != null)
                {
                    if (string.IsNullOrEmpty(existing.Bio) || string.IsNullOrEmpty(existing.DisplayName))
                    {
                        await FetchActorDataAsync(existing);
                    }
                    return existing;
                }
                
                logger.LogWarning("Actor was null after duplicate key error, this shouldn't happen: {ActorUri}", actorUri);
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting actor from Webfinger: {Uri}", actorUri);
            return null;
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

            var webfingerRequest = new HttpRequestMessage(HttpMethod.Get, webfingerUrl);
            webfingerRequest.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");
            webfingerRequest.Headers.Accept.ParseAdd("application/jrd+json");
            webfingerRequest.Headers.Accept.ParseAdd("application/json");
            var response = await HttpClient.SendAsync(webfingerRequest);
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
                    logger.LogInformation("Mastodon/Misskey API not available for {Domain}, trying NodeInfo", instance.Domain);
                    var nodeinfoSuccess = await FetchNodeInfoMetadataAsync(instance);

                    if (!nodeinfoSuccess)
                    {
                        logger.LogInformation("No compatible API found for {Domain}", instance.Domain);
                        return;
                    }
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
            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");
            request.Headers.Accept.ParseAdd("application/activity+json");
            request.Headers.Accept.ParseAdd("application/json");
            var response = await HttpClient.SendAsync(request);

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
            var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");
            request.Headers.Accept.ParseAdd("application/activity+json");
            var response = await HttpClient.SendAsync(request);

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

    private async Task<bool> FetchNodeInfoMetadataAsync(SnFediverseInstance instance)
    {
        try
        {
            var discoveryUrl = $"https://{instance.Domain}/.well-known/nodeinfo";
            var request = new HttpRequestMessage(HttpMethod.Get, discoveryUrl);
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");
            request.Headers.Accept.ParseAdd("application/json");

            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("NodeInfo discovery not available for {Domain} (Status: {StatusCode})",
                    instance.Domain, response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var discovery = JsonSerializer.Deserialize<NodeInfoDiscoveryResponse>(content);

            if (discovery?.Links == null || discovery.Links.Count == 0)
            {
                logger.LogDebug("No NodeInfo links found for {Domain}", instance.Domain);
                return false;
            }

            var nodeinfoLink = discovery.Links
                .FirstOrDefault(l => l.Rel == "http://nodeinfo.diaspora.software/ns/schema/2.1"
                                  || l.Rel == "http://nodeinfo.diaspora.software/ns/schema/2.0");

            if (nodeinfoLink?.Href == null)
            {
                logger.LogDebug("No suitable NodeInfo link found for {Domain}", instance.Domain);
                return false;
            }

            var nodeinfoRequest = new HttpRequestMessage(HttpMethod.Get, nodeinfoLink.Href);
            nodeinfoRequest.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");
            nodeinfoRequest.Headers.Accept.ParseAdd("application/json");

            var nodeinfoResponse = await HttpClient.SendAsync(nodeinfoRequest);

            if (!nodeinfoResponse.IsSuccessStatusCode)
            {
                logger.LogDebug("Failed to fetch NodeInfo document for {Domain} (Status: {StatusCode})",
                    instance.Domain, nodeinfoResponse.StatusCode);
                return false;
            }

            var nodeinfoContent = await nodeinfoResponse.Content.ReadAsStringAsync();
            var nodeinfo = JsonSerializer.Deserialize<NodeInfoResponse>(nodeinfoContent);

            if (nodeinfo == null)
            {
                logger.LogWarning("Failed to parse NodeInfo response for {Domain}", instance.Domain);
                return false;
            }

            instance.Name = instance.Domain;
            instance.Software = nodeinfo.Software?.Name ?? "unknown";
            instance.Version = nodeinfo.Software?.Version;
            instance.Description = $"Users: {nodeinfo.Usage?.Users?.Total ?? 0}, Posts: {nodeinfo.Usage?.LocalPosts ?? 0}";

            if (nodeinfo.Usage?.Users != null)
            {
                instance.ActiveUsers = nodeinfo.Usage.Users.ActiveMonth ?? nodeinfo.Usage.Users.Total;
            }

            if (nodeinfo.Configuration?.Statuses != null)
            {
                var metadata = new Dictionary<string, object>();
                if (nodeinfo.Configuration.Statuses.MaxCharacters.HasValue)
                    metadata["max_note_text_length"] = nodeinfo.Configuration.Statuses.MaxCharacters.Value;
                if (nodeinfo.Configuration.Statuses.MaxMediaAttachments.HasValue)
                    metadata["max_media_attachments"] = nodeinfo.Configuration.Statuses.MaxMediaAttachments.Value;
                if (metadata.Count > 0)
                    instance.Metadata = metadata;
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch NodeInfo metadata for {Domain}", instance.Domain);
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
            .IgnoreQueryFilters()
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (existingActor != null)
        {
            if (existingActor.DeletedAt != null)
            {
                existingActor.DeletedAt = null;
                await db.SaveChangesAsync();
            }
            return existingActor;
        }

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

            try
            {
                db.FediverseActors.Add(actor);
                await db.SaveChangesAsync();
                logger.LogInformation("Successfully stored actor from Webfinger: {Username}@{Domain}", username, domain);
                await FetchActorDataAsync(actor);
                await FetchInstanceMetadataAsync(instance);
                actor.Instance = instance;
                return actor;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                logger.LogInformation("Actor already exists (race condition), fetching: {ActorUri}", actorUri);
                var existing = await db.FediverseActors
                    .IgnoreQueryFilters()
                    .Include(a => a.Instance)
                    .FirstOrDefaultAsync(a => a.Uri == actorUri);
                
                if (existing != null)
                {
                    if (existing.DeletedAt != null)
                    {
                        existing.DeletedAt = null;
                        await db.SaveChangesAsync();
                    }
                    if (string.IsNullOrEmpty(existing.PublicKey))
                    {
                        await FetchActorDataAsync(existing);
                    }
                    existing.Instance = instance;
                    return existing;
                }
                
                logger.LogWarning("Actor was null after duplicate key error, this shouldn't happen: {ActorUri}", actorUri);
                return null;
            }
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
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");
            request.Headers.Accept.ParseAdd("application/activity+json");
            request.Headers.Date = DateTimeOffset.UtcNow;
            request.Headers.Host = new Uri(actor.Uri).Host;

            logger.LogDebug("Fetching actor with headers - Host: {Host}, Date: {Date}, Accept: {Accept}",
                new Uri(actor.Uri).Host, request.Headers.Date, request.Headers.Accept);

            await SignRequestAsync(request);

            logger.LogDebug("Request signed, sending to {Url}", actor.Uri);

            var response = await HttpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var fetchDomain = actor.Uri != null ? new Uri(actor.Uri).Host : null;
                metricsService?.RecordFetch(false, fetchDomain);

                var responseHeaders = string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
                var responseBody = await response.Content.ReadAsStringAsync();
                
                logger.LogWarning("Actor fetch returned {StatusCode} for {Url}. Response headers: {Headers}. Body: {Body}",
                    response.StatusCode, actor.Uri, responseHeaders, responseBody.Length > 500 ? responseBody[..500] + "..." : responseBody);

                if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                {
                    logger.LogWarning("Actor fetch returned {StatusCode} for {Url}, cleaning up actor and all related data",
                        response.StatusCode, actor.Uri);
                    await DeleteActorAndRelatedDataAsync(actor);
                    return;
                }

                logger.LogWarning("Failed to fetch actor data: {Url} - {StatusCode}, using Webfinger data only",
                    actor.Uri, response.StatusCode);
                return;
            }

            var successDomain = actor.Uri != null ? new Uri(actor.Uri).Host : null;
            metricsService?.RecordFetch(true, successDomain);

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

            // Extract follower/following counts from ActivityPub response
            if (int.TryParse(actorData.GetValueOrDefault("followersCount")?.ToString(), out var followersCount))
                actor.FollowersCount = followersCount;
            if (int.TryParse(actorData.GetValueOrDefault("followingCount")?.ToString(), out var followingCount))
                actor.FollowingCount = followingCount;

            // Extract total post count from ActivityPub response (Mastodon uses statusesCount)
            if (int.TryParse(actorData.GetValueOrDefault("statusesCount")?.ToString(), out var statusesCount))
                actor.TotalPostCount = statusesCount;

            // Fetch stats from collection endpoints if not already populated
            await FetchActorStatsAsync(actor);

            var excludedKeys = new HashSet<string>
            {
                "id", "name", "summary", "preferredUsername", "inbox", "outbox", "followers", "following", "featured",
                "icon", "image", "publicKey", "type", "manuallyApprovesFollowers", "discoverable", "@context",
                "followersCount", "followingCount"
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

    private async Task DeleteActorAndRelatedDataAsync(SnFediverseActor actor)
    {
        var postsWithActor = await db.Posts
            .Where(p => p.ActorId == actor.Id)
            .ToListAsync();
        if (postsWithActor.Count > 0)
        {
            logger.LogInformation("Deleting {Count} posts from gone actor: {ActorUri}", postsWithActor.Count, actor.Uri);
            db.Posts.RemoveRange(postsWithActor);
        }

        db.FediverseActors.Remove(actor);
        await db.SaveChangesAsync();
        logger.LogInformation("Successfully cleaned up actor and related data: {ActorUri}", actor.Uri);
    }

    /// <summary>
    /// Fetch actor stats (followers, following, posts count) from ActivityPub collection endpoints.
    /// This is more reliable than using counts from the actor object itself.
    /// </summary>
    public async Task FetchActorStatsAsync(SnFediverseActor actor)
    {
        try
        {
            var tasks = new List<Task>();

            // Fetch followers count
            if (!string.IsNullOrEmpty(actor.FollowersUri) && actor.FollowersCount == 0)
            {
                tasks.Add(FetchCollectionCountAsync(actor.FollowersUri, actor.Uri)
                    .ContinueWith(t => { if (t.IsCompletedSuccessfully && t.Result.HasValue) actor.FollowersCount = t.Result.Value; }));
            }

            // Fetch following count
            if (!string.IsNullOrEmpty(actor.FollowingUri) && actor.FollowingCount == 0)
            {
                tasks.Add(FetchCollectionCountAsync(actor.FollowingUri, actor.Uri)
                    .ContinueWith(t => { if (t.IsCompletedSuccessfully && t.Result.HasValue) actor.FollowingCount = t.Result.Value; }));
            }

            // Fetch post count from outbox
            if (!string.IsNullOrEmpty(actor.OutboxUri) && actor.TotalPostCount == null)
            {
                tasks.Add(FetchCollectionCountAsync(actor.OutboxUri, actor.Uri)
                    .ContinueWith(t => { if (t.IsCompletedSuccessfully && t.Result.HasValue) actor.TotalPostCount = t.Result.Value; }));
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
                logger.LogDebug("Fetched stats for {Username}: followers={Followers}, following={Following}, posts={Posts}",
                    actor.Username, actor.FollowersCount, actor.FollowingCount, actor.TotalPostCount);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch actor stats for {Uri}", actor.Uri);
        }
    }

    private async Task<int?> FetchCollectionCountAsync(string collectionUri, string actorUri)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, collectionUri);
            request.Headers.Accept.ParseAdd("application/activity+json");
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");
            request.Headers.Date = DateTimeOffset.UtcNow;
            request.Headers.Host = new Uri(collectionUri).Host;

            await SignRequestAsync(request);

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Failed to fetch collection count from {Uri}: {Status}", collectionUri, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            
            if (data != null && int.TryParse(data.GetValueOrDefault("totalItems")?.ToString(), out var count))
                return count;

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to fetch collection count from {Uri}", collectionUri);
            return null;
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

    public async Task<Dictionary<string, object>?> FetchActivityAsync(string uri, string? actorUri = null)
    {
        try
        {
            logger.LogInformation("Fetching activity from {Uri}", uri);

            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
            request.Headers.Accept.ParseAdd("application/activity+json");
            request.Headers.Add("User-Agent", $"DysonNetwork/1.0 (https://{Domain})");

            request.Headers.Date = DateTimeOffset.UtcNow;
            request.Headers.Host = new Uri(uri).Host;
            
            logger.LogDebug("Fetching activity with headers - Host: {Host}, Date: {Date}, Accept: {Accept}",
                new Uri(uri).Host, request.Headers.Date, request.Headers.Accept);

            await SignRequestAsync(request);

            logger.LogDebug("Request signed, sending to {Url}", uri);

            var response = await HttpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var responseHeaders = string.Join(", ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"));
                var responseBody = await response.Content.ReadAsStringAsync();
                
                logger.LogWarning("Failed to fetch activity from {Uri}: {StatusCode}. Response headers: {Headers}. Body: {Body}",
                    uri, response.StatusCode, responseHeaders, responseBody.Length > 500 ? responseBody[..500] + "..." : responseBody);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var activity = JsonSerializer.Deserialize<Dictionary<string, object>>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (activity == null)
            {
                logger.LogWarning("Failed to parse activity from {Uri}", uri);
                return null;
            }

            return activity;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching activity from {Uri}", uri);
            return null;
        }
    }

    public async Task<SnFediverseActor?> GetOrCreateActorAsync(string actorUri, string? username = null, Guid? instanceId = null)
    {
        var actor = await db.FediverseActors
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor != null)
        {
            if (actor.DeletedAt != null)
            {
                actor.DeletedAt = null;
                await db.SaveChangesAsync();
            }
            return actor;
        }

        return null;
    }

    public async Task<SnFediverseActor> GetOrCreateActorWithDataAsync(string actorUri, string username, Guid instanceId)
    {
        var actor = await db.FediverseActors
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Uri == actorUri);

        if (actor != null)
        {
            if (actor.DeletedAt != null)
            {
                actor.DeletedAt = null;
                await db.SaveChangesAsync();
            }
            if (string.IsNullOrEmpty(actor.PublicKey))
            {
                await FetchActorDataAsync(actor);
            }
            return actor;
        }

        actor = new SnFediverseActor
        {
            Uri = actorUri,
            Username = username,
            InstanceId = instanceId,
            LastFetchedAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
        };

        try
        {
            db.FediverseActors.Add(actor);
            await db.SaveChangesAsync();
            await FetchActorDataAsync(actor);
            return actor;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
        {
            actor = await db.FediverseActors
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Uri == actorUri);

            if (actor != null)
            {
                if (actor.DeletedAt != null)
                {
                    actor.DeletedAt = null;
                    await db.SaveChangesAsync();
                }
                if (string.IsNullOrEmpty(actor.PublicKey))
                {
                    await FetchActorDataAsync(actor);
                }
                return actor;
            }

            logger.LogWarning("Actor was null after duplicate key error: {ActorUri}", actorUri);
            throw new InvalidOperationException($"Failed to get or create actor: {actorUri}");
        }
    }

    [GeneratedRegex(@"^@?(\w+)@([\w.-]+)$", RegexOptions.Compiled)]
    private static partial Regex HandleRegex();
}
