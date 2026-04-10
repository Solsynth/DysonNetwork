using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.ActivityPub;

[Route("")]
[AllowAnonymous]
public class ServerActorController(
    IServerSigningKeyService serverKeyService,
    IConfiguration configuration,
    ILogger<ServerActorController> logger
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpGet("actor")]
    [Produces("application/activity+json")]
    public async Task<IActionResult> GetServerActor()
    {
        var publicKey = await serverKeyService.GetPublicKeyAsync();
        if (publicKey == null)
        {
            return NotFound(new { error = "Server key not initialized" });
        }

        logger.LogDebug("Serving server actor document for {Domain}", Domain);

        return Ok(new
        {
            @context = new[]
            {
                "https://www.w3.org/ns/activitystreams",
                "https://w3id.org/security/v1"
            },
            id = serverKeyService.ActorUri,
            type = "Application",
            preferredUsername = "server",
            name = "DysonNetwork Server",
            summary = $"The server node for {Domain}",
            url = $"https://{Domain}",
            inbox = $"https://{Domain}/api/inbox",
            outbox = $"https://{Domain}/actor/outbox",
            followers = $"https://{Domain}/actor/followers",
            publicKey = new
            {
                id = serverKeyService.KeyId,
                owner = serverKeyService.ActorUri,
                publicKeyPem = publicKey
            },
            alsoKnownAs = new[] { $"https://{Domain}/" },
            instanceActor = true
        });
    }

    [HttpGet("actor/outbox")]
    [Produces("application/activity+json")]
    public IActionResult GetServerOutbox()
    {
        return Ok(new
        {
            @context = "https://www.w3.org/ns/activitystreams",
            id = $"{serverKeyService.ActorUri}/outbox",
            type = "OrderedCollection",
            totalItems = 0,
            first = $"{serverKeyService.ActorUri}/outbox?page=true",
            orderedItems = Array.Empty<object>()
        });
    }

    [HttpGet("actor/followers")]
    [Produces("application/activity+json")]
    public IActionResult GetServerFollowers()
    {
        return Ok(new
        {
            @context = "https://www.w3.org/ns/activitystreams",
            id = $"{serverKeyService.ActorUri}/followers",
            type = "OrderedCollection",
            totalItems = 0,
            first = $"{serverKeyService.ActorUri}/followers?page=true",
            orderedItems = Array.Empty<object>()
        });
    }

    [HttpGet("actor/following")]
    [Produces("application/activity+json")]
    public IActionResult GetServerFollowing()
    {
        return Ok(new
        {
            @context = "https://www.w3.org/ns/activitystreams",
            id = $"{serverKeyService.ActorUri}/following",
            type = "OrderedCollection",
            totalItems = 0,
            first = $"{serverKeyService.ActorUri}/following?page=true",
            orderedItems = Array.Empty<object>()
        });
    }
}
