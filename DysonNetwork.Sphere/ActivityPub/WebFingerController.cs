using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Sphere.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace DysonNetwork.Sphere.ActivityPub;

[ApiController]
[Route(".well-known")]
public class WebFingerController(
    AppDatabase db,
    IConfiguration configuration
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpGet("host-meta")]
    [Produces("application/xrd+xml")]
    public IActionResult GetHostMeta()
    {
        var webfingerUrl = $"https://{Domain}/.well-known/webfinger?resource={{uri}}";
        
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<XRD xmlns=""http://docs.oasis-open.org/ns/xri/xrd-1.0"">
  <Link rel=""lrdd"" template=""{webfingerUrl}""/>
</XRD>";

        return Content(xml, "application/xrd+xml");
    }

    [HttpGet("webfinger")]
    [Produces("application/jrd+json")]
    public async Task<ActionResult<WebFingerResponse>> GetWebFinger([FromQuery] string resource)
    {
        if (string.IsNullOrEmpty(resource))
            return BadRequest(new ApiError { Code = "WEBFINGER_RESOURCE_REQUIRED", Message = "Missing resource parameter", Status = 400 });

        if (!resource.StartsWith("acct:"))
            return BadRequest(new ApiError { Code = "WEBFINGER_RESOURCE_INVALID_FORMAT", Message = "Invalid resource format", Status = 400 });

        var account = resource[5..];
        var parts = account.Split('@');
        if (parts.Length != 2)
            return BadRequest(new ApiError { Code = "WEBFINGER_ACCOUNT_INVALID_FORMAT", Message = "Invalid account format", Status = 400 });

        var username = parts[0];
        var domain = parts[1];

        if (domain != Domain)
            return NotFound(new ApiError { Code = "WEBFINGER_DOMAIN_NOT_FOUND", Message = "Domain not found.", Status = 404 });

        var serverUsername = configuration["ActivityPub:ServerActor:PreferredUsername"] ?? "solar-network";
        if (username.Equals(serverUsername, StringComparison.OrdinalIgnoreCase))
        {
            var serverActorUrl = $"https://{Domain}/activitypub/actor";
            var serverResponse = new WebFingerResponse
            {
                Subject = resource,
                Links =
                [
                    new WebFingerLink
                    {
                        Rel = "self",
                        Type = "application/activity+json",
                        Href = serverActorUrl
                    },
                    new WebFingerLink
                    {
                        Rel = "http://webfinger.net/rel/profile-page",
                        Type = "text/html",
                        Href = $"https://{Domain}"
                    }
                ]
            };
            return Ok(serverResponse);
        }

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Name.ToLower() == username.ToLowerInvariant());

        if (publisher == null)
            return NotFound(new ApiError { Code = "WEBFINGER_PUBLISHER_NOT_FOUND", Message = "Publisher not found.", Status = 404 });

        var hasActor = await db.FediverseActors
            .AnyAsync(a => a.PublisherId == publisher.Id);

        if (!hasActor)
            return NotFound(new ApiError { Code = "WEBFINGER_ACTOR_NOT_FOUND", Message = "Actor not found.", Status = 404 });

        var actorUrl = $"https://{Domain}/activitypub/actors/{username}";

        var actorResponse = new WebFingerResponse
        {
            Subject = resource,
            Links =
            [
                new WebFingerLink
                {
                    Rel = "self",
                    Type = "application/activity+json",
                    Href = actorUrl
                },
                new WebFingerLink
                {
                    Rel = "http://webfinger.net/rel/profile-page",
                    Type = "text/html",
                    Href = $"https://{Domain}/publishers/{username}"
                }
            ]
        };

        return Ok(actorResponse);
    }
}

public class WebFingerResponse
{
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = null!;

    [JsonPropertyName("links")]
    public List<WebFingerLink> Links { get; set; } = [];
}

public class WebFingerLink
{
    [JsonPropertyName("rel")]
    public string Rel { get; set; } = null!;

    [JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [JsonPropertyName("href")]
    public string Href { get; set; } = null!;
}
