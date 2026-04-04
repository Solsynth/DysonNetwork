using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

[ApiController]
[Route("/quote-authorizations")]
[AllowAnonymous]
public class QuoteAuthorizationController(
    AppDatabase db,
    ActivityPubDeliveryService deliveryService,
    IConfiguration configuration,
    ILogger<QuoteAuthorizationController> logger
) : ControllerBase
{
    private string Domain => configuration["ActivityPub:Domain"] ?? "localhost";
    private string BaseUrl => $"https://{Domain}";

    [HttpGet("{id:guid}")]
    public async Task<ActionResult> GetQuoteAuthorization(Guid id)
    {
        var auth = await db.QuoteAuthorizations
            .Include(q => q.Author)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (auth == null || !auth.IsValid)
            return NotFound();

        var response = new Dictionary<string, object>
        {
            ["@context"] = new object[]
            {
                "https://www.w3.org/ns/activitystreams",
                new Dictionary<string, object>
                {
                    ["QuoteAuthorization"] = "https://w3id.org/fep/044f#QuoteAuthorization",
                    ["gts"] = "https://gotosocial.org/ns#",
                    ["interactingObject"] = new Dictionary<string, object>
                    {
                        ["@id"] = "gts:interactingObject",
                        ["@type"] = "@id"
                    },
                    ["interactionTarget"] = new Dictionary<string, object>
                    {
                        ["@id"] = "gts:interactionTarget",
                        ["@type"] = "@id"
                    }
                }
            },
            ["type"] = "QuoteAuthorization",
            ["id"] = $"{BaseUrl}/quote-authorizations/{auth.Id}",
            ["attributedTo"] = auth.Author.Uri,
            ["interactingObject"] = auth.InteractingObjectUri,
            ["interactionTarget"] = auth.InteractionTargetUri
        };

        return Ok(response);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult> CreateQuoteAuthorization([FromBody] CreateQuoteAuthorizationRequest request)
    {
        var actor = await db.FediverseActors
            .Include(a => a.Instance)
            .FirstOrDefaultAsync(a => a.PublisherId == GetCurrentUserId());

        if (actor == null)
            return Unauthorized();

        var existingAuth = await db.QuoteAuthorizations
            .FirstOrDefaultAsync(q =>
                q.InteractionTargetUri == request.InteractionTargetUri &&
                q.InteractingObjectUri == request.InteractingObjectUri &&
                q.AuthorId == actor.Id &&
                q.IsValid);

        if (existingAuth != null)
        {
            return Ok(new { id = existingAuth.Id, existing = true });
        }

        var targetPost = await db.Posts
            .FirstOrDefaultAsync(p => p.FediverseUri == request.InteractionTargetUri);

        var quotePost = await db.Posts
            .FirstOrDefaultAsync(p => p.FediverseUri == request.InteractingObjectUri);

        var auth = new SnQuoteAuthorization
        {
            Id = Guid.NewGuid(),
            FediverseUri = $"{BaseUrl}/quote-authorizations/{Guid.NewGuid()}",
            AuthorId = actor.Id,
            InteractingObjectUri = request.InteractingObjectUri,
            InteractionTargetUri = request.InteractionTargetUri,
            TargetPostId = targetPost?.Id,
            QuotePostId = quotePost?.Id,
            IsValid = true
        };

        db.QuoteAuthorizations.Add(auth);
        await db.SaveChangesAsync();

        return Ok(new { id = auth.Id });
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<ActionResult> RevokeQuoteAuthorization(Guid id)
    {
        var actor = await db.FediverseActors
            .FirstOrDefaultAsync(a => a.PublisherId == GetCurrentUserId());

        if (actor == null)
            return Unauthorized();

        var auth = await db.QuoteAuthorizations
            .FirstOrDefaultAsync(q => q.Id == id && q.AuthorId == actor.Id);

        if (auth == null)
            return NotFound();

        auth.IsValid = false;
        auth.RevokedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return Ok();
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("sub")?.Value ?? User.FindFirst("user_id")?.Value;
        return Guid.TryParse(userIdClaim, out var userId) ? userId : Guid.Empty;
    }
}

public class CreateQuoteAuthorizationRequest
{
    public string InteractingObjectUri { get; set; } = null!;
    public string InteractionTargetUri { get; set; } = null!;
}
