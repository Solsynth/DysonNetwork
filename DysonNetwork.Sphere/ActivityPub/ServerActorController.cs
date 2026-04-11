using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Sphere.ActivityPub;

[Route("/activitypub")]
[AllowAnonymous]
public class ServerActorController(
    IServerSigningKeyService serverKeyService,
    IConfiguration configuration,
    ILogger<ServerActorController> logger
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";

    private static readonly JsonSerializerOptions ActivityPubOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    [HttpGet("actor")]
    [Produces("application/activity+json")]
    public async Task GetServerActor()
    {
        var publicKey = await serverKeyService.GetPublicKeyAsync();
        if (publicKey == null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = "Server key not initialized" });
            return;
        }

        logger.LogDebug("Serving server actor document for {Domain}", Domain);

        var actor = new ServerActorResponse
        {
            Context = ["https://www.w3.org/ns/activitystreams", "https://w3id.org/security/v1"],
            Id = serverKeyService.ActorUri,
            Summary = $"The server node for {Domain}",
            Url = $"https://{Domain}",
            Inbox = $"{serverKeyService.ActorUri}/inbox",
            Outbox = $"{serverKeyService.ActorUri}/outbox",
            Followers = $"{serverKeyService.ActorUri}/followers",
            PublicKey = new PublicKeyResponse
            {
                Id = serverKeyService.KeyId,
                Owner = serverKeyService.ActorUri,
                PublicKeyPem = publicKey,
            },
            AlsoKnownAs = [$"https://{Domain}/"],
        };

        Response.ContentType = "application/activity+json; charset=utf-8";
        await Response.WriteAsync(JsonSerializer.Serialize(actor, ActivityPubOptions));
    }

    [HttpGet("actor/outbox")]
    [Produces("application/activity+json")]
    public Task GetServerOutbox()
    {
        var collection = new OrderedCollectionResponse
        {
            Id = $"{serverKeyService.ActorUri}/outbox",
            First = $"{serverKeyService.ActorUri}/outbox?page=true",
        };

        Response.ContentType = "application/activity+json; charset=utf-8";
        return Response.WriteAsync(JsonSerializer.Serialize(collection, ActivityPubOptions));
    }

    [HttpGet("actor/followers")]
    [Produces("application/activity+json")]
    public Task GetServerFollowers()
    {
        var collection = new OrderedCollectionResponse
        {
            Id = $"{serverKeyService.ActorUri}/followers",
            First = $"{serverKeyService.ActorUri}/followers?page=true",
        };

        Response.ContentType = "application/activity+json; charset=utf-8";
        return Response.WriteAsync(JsonSerializer.Serialize(collection, ActivityPubOptions));
    }

    [HttpGet("actor/following")]
    [Produces("application/activity+json")]
    public Task GetServerFollowing()
    {
        var collection = new OrderedCollectionResponse
        {
            Id = $"{serverKeyService.ActorUri}/following",
            First = $"{serverKeyService.ActorUri}/following?page=true",
        };

        Response.ContentType = "application/activity+json; charset=utf-8";
        return Response.WriteAsync(JsonSerializer.Serialize(collection, ActivityPubOptions));
    }

    [HttpGet("actor/main-key")]
    [Produces("application/activity+json")]
    public async Task GetServerMainKey()
    {
        var publicKey = await serverKeyService.GetPublicKeyAsync();
        if (publicKey == null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsJsonAsync(new { error = "Server key not initialized" });
            return;
        }

        logger.LogDebug("Serving server main-key");

        var keyDoc = new PublicKeyDocumentResponse
        {
            Id = serverKeyService.KeyId,
            Owner = serverKeyService.ActorUri,
            PublicKeyPem = publicKey,
        };

        Response.ContentType = "application/activity+json; charset=utf-8";
        await Response.WriteAsync(JsonSerializer.Serialize(keyDoc, ActivityPubOptions));
    }
}
