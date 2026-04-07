using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using PublisherMemberRole = DysonNetwork.Shared.Models.PublisherMemberRole;

namespace DysonNetwork.Sphere.Account;

[ApiController]
[Route("api/account/publishing")]
[Authorize]
public class PublishingSettingsController(
    AppDatabase db,
    RemoteAccountService remoteAccounts,
    ActivityPub.ActivityPubObjectFactory objFactory,
    ILogger<PublishingSettingsController> logger
) : ControllerBase
{
    public class PublishingSettingsRequest
    {
        public Guid? DefaultPostingPublisherId { get; set; }
        public Guid? DefaultReplyPublisherId { get; set; }
        public Guid? DefaultFediversePublisherId { get; set; }
    }

    [HttpGet]
    public async Task<ActionResult<SnPublishingSettings>> GetSettings()
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var settings = await db.PublishingSettings
            .FirstOrDefaultAsync(s => s.AccountId == accountId);

        if (settings == null)
        {
            settings = new SnPublishingSettings { AccountId = accountId };
            db.PublishingSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        return Ok(settings);
    }

    [HttpPatch]
    public async Task<ActionResult<SnPublishingSettings>> UpdateSettings([FromBody] PublishingSettingsRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not DyAccount currentUser)
            return Unauthorized();

        var accountId = Guid.Parse(currentUser.Id);

        var settings = await db.PublishingSettings
            .FirstOrDefaultAsync(s => s.AccountId == accountId);

        if (settings == null)
        {
            settings = new SnPublishingSettings { AccountId = accountId };
            db.PublishingSettings.Add(settings);
        }

        if (request.DefaultPostingPublisherId != null)
        {
            var publisher = await db.Publishers
                .FirstOrDefaultAsync(p => p.Id == request.DefaultPostingPublisherId);
            if (publisher == null)
                return BadRequest("Publisher not found");
            if (!await IsMemberAsync(publisher.Id, accountId))
                return BadRequest("You are not a member of this publisher");
            settings.DefaultPostingPublisherId = request.DefaultPostingPublisherId;
        }

        if (request.DefaultReplyPublisherId != null)
        {
            var publisher = await db.Publishers
                .FirstOrDefaultAsync(p => p.Id == request.DefaultReplyPublisherId);
            if (publisher == null)
                return BadRequest("Publisher not found");
            if (!await IsMemberAsync(publisher.Id, accountId))
                return BadRequest("You are not a member of this publisher");
            settings.DefaultReplyPublisherId = request.DefaultReplyPublisherId;
        }

        if (request.DefaultFediversePublisherId != null)
        {
            var publisher = await db.Publishers
                .FirstOrDefaultAsync(p => p.Id == request.DefaultFediversePublisherId);
            if (publisher == null)
                return BadRequest("Publisher not found");
            if (!await IsMemberAsync(publisher.Id, accountId))
                return BadRequest("You are not a member of this publisher");

            var actor = await objFactory.GetLocalActorAsync(request.DefaultFediversePublisherId.Value);
            if (actor == null)
                return BadRequest("Fediverse is not enabled for this publisher");
            settings.DefaultFediversePublisherId = request.DefaultFediversePublisherId;
        }

        settings.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        logger.LogInformation("Updated account settings for {AccountId}", accountId);

        return Ok(settings);
    }

    private async Task<bool> IsMemberAsync(Guid publisherId, Guid accountId)
    {
        return await db.PublisherMembers
            .AnyAsync(m => m.PublisherId == publisherId && m.AccountId == accountId && m.JoinedAt != null);
    }
}
