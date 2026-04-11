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

    [HttpGet("activitypub/actor")]
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
            inbox = $"{serverKeyService.ActorUri}/inbox",
            outbox = $"{serverKeyService.ActorUri}/outbox",
            followers = $"{serverKeyService.ActorUri}/followers",
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

    [HttpGet("activitypub/actor/outbox")]
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

    [HttpGet("activitypub/actor/followers")]
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

    [HttpGet("activitypub/actor/following")]
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

    [HttpGet("activitypub/actor/main-key")]
    [Produces("application/activity+json")]
    public async Task<IActionResult> GetServerMainKey()
    {
        var publicKey = await serverKeyService.GetPublicKeyAsync();
        if (publicKey == null)
        {
            return NotFound(new { error = "Server key not initialized" });
        }

        logger.LogDebug("Serving server main-key");

        return Ok(new
        {
            @context = new[]
            {
                "https://w3id.org/security/v1",
                "https://www.w3.org/ns/activitystreams",
            },
            id = serverKeyService.KeyId,
            owner = serverKeyService.ActorUri,
            publicKeyPem = publicKey,
            type = "RsaSignature2017",
        });
    }
}
