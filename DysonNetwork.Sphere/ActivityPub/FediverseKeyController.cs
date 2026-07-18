using DysonNetwork.Shared.Networking;
using DysonNetwork.Sphere.ActivityPub.Services;
using DysonNetwork.Sphere.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.ActivityPub;

[Route("activitypub/actors/{username}")]
[AllowAnonymous]
public class FediverseKeyController(
    AppDatabase db,
    IKeyService keyService,
    ILogger<FediverseKeyController> logger,
    IConfiguration configuration
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    [HttpGet("main-key")]
    [Produces("application/activity+json")]
    public async Task<IActionResult> GetMainKey(string username)
    {
        var publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name.ToLower() == username.ToLowerInvariant());

        if (publisher == null)
            return NotFound();

        var actor = await db.FediverseActors.FirstOrDefaultAsync(a =>
            a.PublisherId == publisher.Id
        );

        if (actor == null)
            return NotFound();

        var key = await keyService.GetKeyForActorAsync(actor.Id);
        if (key == null)
            return NotFound(new ApiError { Code = "FEDIVERSE_KEY_NOT_FOUND", Message = "No key found for this actor", Status = 404 });

        var actorUrl = $"https://{Domain}/activitypub/actors/{username}";
        var keyId = $"{actorUrl}#main-key";

        logger.LogDebug("Serving main-key for {Username}: {KeyId}", username, keyId);

        return Ok(
            new
            {
                @context = new[]
                {
                    "https://w3id.org/security/v1",
                    "https://www.w3.org/ns/activitystreams",
                },
                id = keyId,
                owner = actorUrl,
                publicKeyPem = key.KeyPem,
                type = "RsaSignature2017",
            }
        );
    }

    [HttpGet("publickey")]
    [Produces("application/activity+json")]
    public async Task<IActionResult> GetPublicKey(string username)
    {
        return await GetMainKey(username);
    }
}

public class PublicKeyDocument
{
    public string[]? Context { get; set; }
    public string Id { get; set; } = null!;
    public string Owner { get; set; } = null!;
    public string PublicKeyPem { get; set; } = null!;
    public string Type { get; set; } = "RsaSignature2017";
}

