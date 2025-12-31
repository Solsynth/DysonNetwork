using Microsoft.AspNetCore.Mvc;
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

    [HttpGet("webfinger")]
    [Produces("application/jrd+json")]
    public async Task<ActionResult<WebFingerResponse>> GetWebFinger([FromQuery] string resource)
    {
        if (string.IsNullOrEmpty(resource))
            return BadRequest("Missing resource parameter");

        if (!resource.StartsWith("acct:"))
            return BadRequest("Invalid resource format");

        var account = resource[5..];
        var parts = account.Split('@');
        if (parts.Length != 2)
            return BadRequest("Invalid account format");

        var username = parts[0];
        var domain = parts[1];

        if (domain != Domain)
            return NotFound();

        var publisher = await db.Publishers
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Name == username);

        if (publisher == null)
            return NotFound();

        var hasActor = await db.FediverseActors
            .AnyAsync(a => a.PublisherId == publisher.Id);

        if (!hasActor)
            return NotFound();

        var actorUrl = $"https://{Domain}/activitypub/actors/{username}";

        var response = new WebFingerResponse
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

        return Ok(response);
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
